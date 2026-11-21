using HospitalManagement.Api.Data;
using HospitalManagement.Api.DTOs;
using HospitalManagement.Api.Exceptions;
using HospitalManagement.Api.Helpers;
using HospitalManagement.Api.Interfaces;
using HospitalManagement.Api.Models;
using HospitalManagement.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagement.Api.Services;

/// <summary>
/// Driver workflow + GPS ingestion. The driver is ALWAYS the authenticated user; the service looks up that user's own active
/// assignment and never accepts an assignment, ambulance or driver id from the caller.
/// </summary>
public class AmbulanceTrackingService(
    AppDbContext dbContext,
    IAmbulanceBroadcaster broadcaster,
    AmbulancePublisher publisher) : IAmbulanceTrackingService
{
    public async Task<DriverAssignmentDto?> GetMyAssignmentAsync(Guid driverUserId, CancellationToken cancellationToken)
    {
        var assignment = await LoadAsync(driverUserId, tracking: false, cancellationToken);
        return assignment is null ? null : ToDto(assignment);
    }

    public Task<DriverAssignmentDto?> AcceptAsync(Guid driverUserId, AuditActor actor, CancellationToken cancellationToken) =>
        StepAsync(driverUserId, actor, "AmbulanceAssignmentAccepted", cancellationToken, (a, request) =>
        {
            Require(a, AssignmentStatus.Pending, "accept");
            a.Status = AssignmentStatus.Accepted;
            a.AcceptedAt = DateTime.UtcNow;
        });

    public Task<DriverAssignmentDto?> DeclineAsync(Guid driverUserId, string? reason, AuditActor actor, CancellationToken cancellationToken) =>
        StepAsync(driverUserId, actor, "AmbulanceAssignmentDeclined", cancellationToken, (a, request) =>
        {
            if (a.Status is not (AssignmentStatus.Pending or AssignmentStatus.Accepted))
                throw new ConflictException("The trip has already started and cannot be declined.");
            a.Status = AssignmentStatus.Declined;
            a.IsActive = false;
            a.EndedAt = DateTime.UtcNow;
            a.DeclineReason = AmbulanceRules.Trim(reason, 500);
            // The ambulance becomes free and the request goes back to the dispatch queue.
            AmbulanceRules.MoveAmbulance(dbContext, a.Ambulance, AmbulanceStatus.Available, a.DriverUserId, "Driver declined");
            AmbulanceRules.MoveRequest(dbContext, request, AmbulanceRequestStatus.Accepted, a.DriverUserId, "Driver declined - needs a new ambulance");
        });

    public Task<DriverAssignmentDto?> StartTripAsync(Guid driverUserId, AuditActor actor, CancellationToken cancellationToken) =>
        StepAsync(driverUserId, actor, "AmbulanceTripStarted", cancellationToken, (a, request) =>
        {
            Require(a, AssignmentStatus.Accepted, "start the trip");
            a.Status = AssignmentStatus.EnRoute;
            a.StartedAt = DateTime.UtcNow;
            AmbulanceRules.MoveRequest(dbContext, request, AmbulanceRequestStatus.Dispatched, a.DriverUserId);
            AmbulanceRules.MoveAmbulance(dbContext, a.Ambulance, AmbulanceStatus.Dispatched, a.DriverUserId);
        });

    public Task<DriverAssignmentDto?> ArrivedAsync(Guid driverUserId, AuditActor actor, CancellationToken cancellationToken) =>
        StepAsync(driverUserId, actor, "AmbulanceArrivedAtScene", cancellationToken, (a, request) =>
        {
            Require(a, AssignmentStatus.EnRoute, "mark arrival");
            a.Status = AssignmentStatus.Arrived;
            a.ArrivedAt = DateTime.UtcNow;
            AmbulanceRules.MoveRequest(dbContext, request, AmbulanceRequestStatus.Arrived, a.DriverUserId);
        },
        afterCommit: a => broadcaster.AmbulanceArrivedAsync(a.RequestId, a.Request.RequestNumber, a.ArrivedAt ?? DateTime.UtcNow));

    public Task<DriverAssignmentDto?> PatientPickedUpAsync(Guid driverUserId, AuditActor actor, CancellationToken cancellationToken) =>
        StepAsync(driverUserId, actor, "AmbulancePatientPickedUp", cancellationToken, (a, request) =>
        {
            Require(a, AssignmentStatus.Arrived, "mark the patient as picked up");
            a.Status = AssignmentStatus.PickedUp;
            a.PickedUpAt = DateTime.UtcNow;
            AmbulanceRules.MoveRequest(dbContext, request, AmbulanceRequestStatus.PatientPickedUp, a.DriverUserId);
            AmbulanceRules.MoveAmbulance(dbContext, a.Ambulance, AmbulanceStatus.OnTrip, a.DriverUserId);
        });

    public Task<DriverAssignmentDto?> CompleteAsync(Guid driverUserId, AuditActor actor, CancellationToken cancellationToken) =>
        StepAsync(driverUserId, actor, "AmbulanceTripCompleted", cancellationToken, (a, request) =>
        {
            Require(a, AssignmentStatus.PickedUp, "complete the trip");
            a.Status = AssignmentStatus.Completed;
            a.IsActive = false;                        // tracking stops: no active assignment means no GPS is accepted
            a.CompletedAt = a.EndedAt = DateTime.UtcNow;
            AmbulanceRules.MoveRequest(dbContext, request, AmbulanceRequestStatus.AtHospital, a.DriverUserId);
            AmbulanceRules.MoveAmbulance(dbContext, a.Ambulance, AmbulanceStatus.Available, a.DriverUserId, "Trip completed");
        });

    public async Task<DriverLocationResultDto> UpdateLocationAsync(Guid driverUserId, DriverLocationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        AmbulanceRules.ValidateCoordinates(request.Latitude, request.Longitude);
        if (request.AccuracyMeters is { } accuracy && (double.IsNaN(accuracy) || accuracy < 0 || accuracy > 100_000))
            throw new RequestValidationException("Accuracy must be between 0 and 100000 metres.");
        if (request.SpeedKmh is { } speed && (double.IsNaN(speed) || speed < 0 || speed > 400))
            throw new RequestValidationException("Speed must be between 0 and 400 km/h.");
        if (request.Heading is { } heading && (double.IsNaN(heading) || heading < 0 || heading > 360))
            throw new RequestValidationException("Heading must be between 0 and 360 degrees.");

        var now = DateTime.UtcNow;
        if (request.ClientTimestamp is { } clientTime && now - clientTime.ToUniversalTime() > AmbulanceRules.MaxClientFixAge)
            return new DriverLocationResultDto(false, "stale", now);          // an old buffered fix must not look live

        var assignment = await LoadAsync(driverUserId, tracking: true, cancellationToken)
            ?? throw new ConflictException("You have no active assignment.");
        if (!AmbulanceRules.IsTripActive(assignment.Status))
            throw new ConflictException("Location is recorded only while the trip is under way. Start the trip first.");

        var ambulance = assignment.Ambulance;
        if (ambulance.LastLocationAt is { } last && now - last < AmbulanceRules.MinLocationInterval)
            return new DriverLocationResultDto(false, "throttled", now);

        // Server clock only: the device clock is never trusted as the receipt time.
        ambulance.CurrentLatitude = request.Latitude;
        ambulance.CurrentLongitude = request.Longitude;
        ambulance.LocationAccuracyMeters = request.AccuracyMeters;
        ambulance.LocationSpeedKmh = request.SpeedKmh;
        ambulance.LastLocationAt = now;
        dbContext.AmbulanceLocationHistories.Add(new AmbulanceLocationHistory
        {
            Id = Guid.NewGuid(), AssignmentId = assignment.Id, AmbulanceId = ambulance.Id,
            Latitude = request.Latitude!.Value, Longitude = request.Longitude!.Value,
            AccuracyMeters = request.AccuracyMeters, SpeedKmh = request.SpeedKmh, Heading = request.Heading, ReceivedAt = now,
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        double? distance = null;
        int? eta = null;
        if (assignment.Status == AssignmentStatus.EnRoute)
        {
            distance = Math.Round(AmbulanceRules.DistanceKm(request.Latitude!.Value, request.Longitude!.Value, assignment.Request.Latitude, assignment.Request.Longitude), 2);
            eta = AmbulanceRules.EtaMinutes(distance.Value, request.SpeedKmh);
        }

        var location = new AmbulanceLocationDto(request.Latitude!.Value, request.Longitude!.Value, request.AccuracyMeters, request.SpeedKmh, now);
        await broadcaster.LocationChangedAsync(
            new AmbulanceLocationEventDto(ambulance.Id, assignment.RequestId, assignment.Id, ambulance.VehicleNumber, location),
            new PublicLocationUpdateDto(new PublicLocationDto(location.Latitude, location.Longitude, location.AccuracyMeters, location.SpeedKmh, now), distance, eta, now),
            assignment.RequestId);

        return new DriverLocationResultDto(true, null, now);
    }

    // ------------------------------------------------------------------ helpers
    private async Task<DriverAssignmentDto?> StepAsync(
        Guid driverUserId,
        AuditActor actor,
        string auditAction,
        CancellationToken cancellationToken,
        Action<AmbulanceAssignment, AmbulanceRequest> apply,
        Func<AmbulanceAssignment, Task>? afterCommit = null)
    {
        var assignment = await LoadAsync(driverUserId, tracking: true, cancellationToken)
            ?? throw new NotFoundException("You have no active assignment.");

        apply(assignment, assignment.Request);
        AuditWriter.Stage(dbContext, actor, auditAction, nameof(AmbulanceAssignment), assignment.Id.ToString(),
            $"Assignment for {assignment.Request.RequestNumber} (ambulance {assignment.Ambulance.VehicleNumber}): {assignment.Status}.");
        await dbContext.SaveChangesAsync(cancellationToken);

        await publisher.RequestChangedAsync(assignment.RequestId, assignment.Id, cancellationToken);
        await publisher.AvailabilityChangedAsync(assignment.Ambulance);
        if (afterCommit is not null) await afterCommit(assignment);

        return assignment.IsActive ? ToDto(assignment) : null;
    }

    private static void Require(AmbulanceAssignment assignment, AssignmentStatus expected, string action)
    {
        if (assignment.Status != expected)
            throw new ConflictException($"You cannot {action} while the assignment is {assignment.Status}.");
    }

    /// <summary>The caller's OWN active assignment (a driver has at most one: unique indexes on ambulance and driver).</summary>
    private async Task<AmbulanceAssignment?> LoadAsync(Guid driverUserId, bool tracking, CancellationToken cancellationToken)
    {
        var source = tracking ? dbContext.AmbulanceAssignments : dbContext.AmbulanceAssignments.AsNoTracking();
        return await source.Include(a => a.Request).Include(a => a.Ambulance)
            .Where(a => a.DriverUserId == driverUserId && a.IsActive)
            .OrderByDescending(a => a.AssignedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static DriverAssignmentDto ToDto(AmbulanceAssignment a) => new(
        a.Id, a.Status, a.Request.RequestNumber, a.Request.Status, a.Request.Latitude, a.Request.Longitude, a.Request.Address,
        a.Request.EmergencyType, a.Request.InjuredCount, a.Request.RequesterName, a.Request.ContactPhone, a.Request.Description,
        a.Ambulance.VehicleNumber, a.Ambulance.Type, a.AssignedAt, DateTime.UtcNow, AmbulanceRules.IsTripActive(a.Status));
}
