using HospitalManagement.Api.Data;
using HospitalManagement.Api.DTOs;
using HospitalManagement.Api.Exceptions;
using HospitalManagement.Api.Helpers;
using HospitalManagement.Api.Interfaces;
using HospitalManagement.Api.Models;
using HospitalManagement.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HospitalManagement.Api.Services;

/// <summary>
/// Driver workflow + GPS ingestion. The driver is ALWAYS the authenticated user; the service looks up that user's own active
/// assignment and never accepts an assignment, ambulance or driver id from the caller.
/// <para>
/// GPS-assisted workflow: accepting starts the trip and tracking; arrival at the pickup point and at the hospital is recorded by
/// the SERVER from stored GPS fixes (see <see cref="Geofence"/>), never because the browser says so. The driver confirms only what
/// GPS cannot know: that the patient is on board, and that the patient was handed over. Manual buttons remain as a fallback.
/// </para>
/// </summary>
public class AmbulanceTrackingService(
    AppDbContext dbContext,
    IAmbulanceBroadcaster broadcaster,
    AmbulancePublisher publisher,
    IOptionsMonitor<AmbulanceAutomationOptions>? automationOptions = null,
    TimeProvider? timeProvider = null,
    ILogger<AmbulanceTrackingService>? logger = null) : IAmbulanceTrackingService
{
    private static readonly AmbulanceAutomationOptions DefaultOptions = new();

    /// <summary>
    /// Takes the shared gate of this driver's active trip (see <see cref="AmbulanceGates"/>), so the driver's taps, GPS detection and the
    /// dispatch desk's confirmations for the same request are applied one at a time. With no active trip the driver's own id is used.
    /// </summary>
    private async Task<IDisposable> LockDriverAsync(Guid driverUserId, CancellationToken cancellationToken)
    {
        var requestId = await dbContext.AmbulanceAssignments.AsNoTracking()
            .Where(a => a.DriverUserId == driverUserId && a.IsActive)
            .Select(a => (Guid?)a.RequestId).FirstOrDefaultAsync(cancellationToken);
        return await AmbulanceGates.AcquireAsync(requestId ?? driverUserId, cancellationToken);
    }

    private AmbulanceAutomationOptions Options => automationOptions?.CurrentValue ?? DefaultOptions;

    private DateTime Now() => (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;

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
            a.AcceptedAt = Now();
        });

    public Task<DriverAssignmentDto?> AcceptAndStartAsync(Guid driverUserId, AuditActor actor, CancellationToken cancellationToken) =>
        StepIfChangedAsync(driverUserId, actor, "AmbulanceAssignmentAccepted", cancellationToken, (a, request) =>
        {
            // A repeated tap or a retry after a lost response: the trip is already under way, so change nothing and add no history.
            if (a.Status == AssignmentStatus.EnRoute && a.AcceptedAt is not null) return false;
            if (a.Status is not (AssignmentStatus.Pending or AssignmentStatus.Accepted))
                throw new ConflictException($"You cannot accept while the assignment is {a.Status}.");

            var now = Now();
            a.AcceptedAt ??= now;
            a.Status = AssignmentStatus.EnRoute;     // tracking starts here: GPS is only recorded while the trip is under way
            a.StartedAt = now;
            AmbulanceRules.MoveRequest(dbContext, request, AmbulanceRequestStatus.Dispatched, a.DriverUserId);
            AmbulanceRules.MoveAmbulance(dbContext, a.Ambulance, AmbulanceStatus.Dispatched, a.DriverUserId);
            return true;
        });

    public Task<DriverAssignmentDto?> DeclineAsync(Guid driverUserId, string? reason, AuditActor actor, CancellationToken cancellationToken) =>
        StepAsync(driverUserId, actor, "AmbulanceAssignmentDeclined", cancellationToken, (a, request) =>
        {
            if (a.Status is not (AssignmentStatus.Pending or AssignmentStatus.Accepted))
                throw new ConflictException("The trip has already started and cannot be declined.");
            a.Status = AssignmentStatus.Declined;
            a.IsActive = false;
            a.EndedAt = Now();
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
            a.StartedAt = Now();
            AmbulanceRules.MoveRequest(dbContext, request, AmbulanceRequestStatus.Dispatched, a.DriverUserId);
            AmbulanceRules.MoveAmbulance(dbContext, a.Ambulance, AmbulanceStatus.Dispatched, a.DriverUserId);
        });

    public Task<DriverAssignmentDto?> ArrivedAsync(Guid driverUserId, AuditActor actor, CancellationToken cancellationToken) =>
        StepIfChangedAsync(driverUserId, actor, "AmbulanceArrivedAtScene", cancellationToken, (a, request) =>
        {
            // GPS got there first (the screen had not refreshed yet): the arrival is already recorded, so this tap changes nothing.
            if (a.Status == AssignmentStatus.Arrived && a.ArrivalMethod == AmbulanceRules.ArrivalAuto) return false;
            Require(a, AssignmentStatus.EnRoute, "mark arrival");
            a.Status = AssignmentStatus.Arrived;
            a.ArrivedAt = Now();
            a.ArrivalMethod = AmbulanceRules.ArrivalManual;          // the driver's fallback: GPS did not (or could not) detect it
            (a.ArrivedLatitude, a.ArrivedLongitude, a.ArrivedAccuracyMeters) = AmbulanceTripSteps.LastKnownPosition(a.Ambulance, Now());
            AmbulanceRules.MoveRequest(dbContext, request, AmbulanceRequestStatus.Arrived, a.DriverUserId);
            return true;
        },
        afterCommit: a => broadcaster.AmbulanceArrivedAsync(a.RequestId, a.Request.RequestNumber, a.ArrivedAt ?? Now()));

    public Task<DriverAssignmentDto?> HospitalArrivedAsync(Guid driverUserId, AuditActor actor, CancellationToken cancellationToken) =>
        StepIfChangedAsync(driverUserId, actor, "AmbulanceArrivedAtHospital", cancellationToken, (a, request) =>
        {
            Require(a, AssignmentStatus.PickedUp, "record the arrival at the hospital");
            if (request.Status == AmbulanceRequestStatus.AtHospital) return false;   // GPS (or an earlier tap) already recorded it
            AmbulanceTripSteps.RecordHospitalArrival(dbContext, a, request, AmbulanceRules.ArrivalManual, Now(),
                AmbulanceTripSteps.LastKnownPosition(a.Ambulance, Now()), a.DriverUserId, "Driver confirmed arrival at the hospital");
            return true;
        });

    public Task<DriverAssignmentDto?> PatientPickedUpAsync(Guid driverUserId, AuditActor actor, CancellationToken cancellationToken) =>
        StepCoreAsync(driverUserId, actor, "AmbulancePatientPickedUp", cancellationToken, async (a, request) =>
        {
            // Dispatch confirmed first (the screen had not refreshed yet): the first valid confirmation wins, this tap changes nothing.
            if (a.Status == AssignmentStatus.PickedUp && a.PickupConfirmedSource == AmbulanceRules.SourceDispatcher) return false;
            Require(a, AssignmentStatus.Arrived, "mark the patient as picked up");
            await AmbulanceTripSteps.ConfirmPickupAsync(dbContext, a, request, AmbulanceRules.SourceDriver, a.DriverUserId, Now(), cancellationToken);
            return true;
        });

    public Task<DriverAssignmentDto?> CompleteAsync(Guid driverUserId, AuditActor actor, CancellationToken cancellationToken) =>
        StepCoreAsync(driverUserId, actor, "AmbulanceTripCompleted", cancellationToken, async (a, request) =>
        {
            Require(a, AssignmentStatus.PickedUp, "complete the trip");
            // Normally GPS (or the fallback button) has already moved the request to AtHospital. If neither happened the handover
            // itself records the arrival, exactly as before. Dispatch still finishes the request (Completed) after the patient is linked.
            await AmbulanceTripSteps.ConfirmHandoverAsync(dbContext, a, request, AmbulanceRules.SourceDriver, a.DriverUserId, Now(), cancellationToken);
            return true;
        });

    public async Task<DriverLocationResultDto> UpdateLocationAsync(Guid driverUserId, DriverLocationRequest request, CancellationToken cancellationToken, AuditActor? actor = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        AmbulanceRules.ValidateCoordinates(request.Latitude, request.Longitude);
        if (request.AccuracyMeters is { } accuracy && (double.IsNaN(accuracy) || accuracy < 0 || accuracy > 100_000))
            throw new RequestValidationException("Accuracy must be between 0 and 100000 metres.");
        if (request.SpeedKmh is { } speed && (double.IsNaN(speed) || speed < 0 || speed > 400))
            throw new RequestValidationException("Speed must be between 0 and 400 km/h.");
        if (request.Heading is { } heading && (double.IsNaN(heading) || heading < 0 || heading > 360))
            throw new RequestValidationException("Heading must be between 0 and 360 degrees.");

        var now = Now();
        if (request.ClientTimestamp is { } clientTime && now - clientTime.ToUniversalTime() > AmbulanceRules.MaxClientFixAge)
            return new DriverLocationResultDto(false, "stale", now);          // an old buffered fix must not look live

        using var driverLock = await LockDriverAsync(driverUserId, cancellationToken);
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

        // Arrival is decided HERE, from the fixes the server has stored: the browser never reports "I have arrived" on its own.
        var arrivalEvent = await TryAutoArrivalAsync(
            assignment, actor ?? new AuditActor(driverUserId, null, AmbulanceRules.DriverRoleName, null), cancellationToken);
        return new DriverLocationResultDto(true, null, now, arrivalEvent);
    }

    // ------------------------------------------------------------------ GPS arrival detection
    /// <summary>Enough recent fixes to cover the dwell time at the server's minimum fix interval, with head-room, but never unbounded.</summary>
    private static int FixesToConsider(GeofenceSettings settings) =>
        Math.Clamp((int)Math.Ceiling(settings.Dwell.TotalSeconds / AmbulanceRules.MinLocationInterval.TotalSeconds) + 10, 20, 1000);

    /// <summary>
    /// Records arrival at the pickup point or at the hospital when - and only when - the stored GPS fixes prove it. Returns the event
    /// name when THIS call recorded an arrival, otherwise null. Never throws into the location update (the fix is already saved).
    /// Idempotent: once the status has moved on, later fixes find no stage to evaluate.
    /// </summary>
    private async Task<string?> TryAutoArrivalAsync(AmbulanceAssignment assignment, AuditActor actor, CancellationToken cancellationToken)
    {
        var options = Options;
        if (!options.Enabled) return null;

        var request = assignment.Request;
        var pickupStage = assignment.Status == AssignmentStatus.EnRoute && request.Status == AmbulanceRequestStatus.Dispatched;
        var hospitalStage = assignment.Status == AssignmentStatus.PickedUp && request.Status == AmbulanceRequestStatus.PatientPickedUp;
        if (!pickupStage && !hospitalStage) return null;
        if (hospitalStage && !options.HasHospitalLocation) return null;           // no destination configured: the driver uses the fallback

        double targetLatitude, targetLongitude;
        GeofenceSettings settings;
        DateTime? stageStart;
        if (pickupStage)
        {
            targetLatitude = request.Latitude;
            targetLongitude = request.Longitude;
            settings = options.PickupSettings();
            stageStart = assignment.StartedAt;
        }
        else
        {
            targetLatitude = options.HospitalLatitude!.Value;
            targetLongitude = options.HospitalLongitude!.Value;
            settings = options.HospitalSettings();
            stageStart = assignment.PickedUpAt;                                  // fixes from the way to the patient do not count
        }

        var query = dbContext.AmbulanceLocationHistories.AsNoTracking().Where(h => h.AssignmentId == assignment.Id);
        if (stageStart is { } start) query = query.Where(h => h.ReceivedAt >= start);
        var recent = await query.OrderByDescending(h => h.ReceivedAt).Take(FixesToConsider(settings)).ToListAsync(cancellationToken);
        var fixes = recent.OrderBy(h => h.ReceivedAt)
            .Select(h => new GeoFix(h.Latitude, h.Longitude, h.AccuracyMeters, h.ReceivedAt)).ToList();

        var result = Geofence.Evaluate(fixes, targetLatitude, targetLongitude, settings);
        if (!result.Arrived || result.Fix is not { } fix) return null;

        var note = $"Detected by GPS: inside the {settings.RadiusMeters:0} m zone since {result.InsideSince:HH:mm:ss} UTC " +
                   $"(accuracy {fix.AccuracyMeters:0} m, {result.DistanceMeters:0} m from the target).";
        try
        {
            if (pickupStage)
            {
                assignment.Status = AssignmentStatus.Arrived;
                assignment.ArrivedAt = fix.At;
                assignment.ArrivalMethod = AmbulanceRules.ArrivalAuto;
                assignment.ArrivedLatitude = fix.Latitude;
                assignment.ArrivedLongitude = fix.Longitude;
                assignment.ArrivedAccuracyMeters = fix.AccuracyMeters;
                AmbulanceRules.MoveRequest(dbContext, request, AmbulanceRequestStatus.Arrived, assignment.DriverUserId, note);
                AuditWriter.Stage(dbContext, actor, "AmbulanceArrivedAtScene", nameof(AmbulanceAssignment), assignment.Id.ToString(),
                    $"Assignment for {request.RequestNumber} (ambulance {assignment.Ambulance.VehicleNumber}): arrival at the pickup point detected by GPS.");
            }
            else
            {
                AmbulanceTripSteps.RecordHospitalArrival(dbContext, assignment, request, AmbulanceRules.ArrivalAuto, fix.At,
                    (fix.Latitude, fix.Longitude, fix.AccuracyMeters), assignment.DriverUserId, note);
                AuditWriter.Stage(dbContext, actor, "AmbulanceArrivedAtHospital", nameof(AmbulanceAssignment), assignment.Id.ToString(),
                    $"Assignment for {request.RequestNumber} (ambulance {assignment.Ambulance.VehicleNumber}): arrival at the hospital detected by GPS.");
            }
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // The position itself is already stored. Leave the status alone: the next fix (or the driver's fallback) will try again.
            logger?.LogWarning(exception, "Could not record the GPS-detected arrival for assignment {AssignmentId}", assignment.Id);
            return null;
        }

        await publisher.RequestChangedAsync(assignment.RequestId, assignment.Id, cancellationToken);
        if (pickupStage)
        {
            await broadcaster.AmbulanceArrivedAsync(assignment.RequestId, request.RequestNumber, fix.At);
            return AutoArrivalEvents.AtPickup;
        }
        return AutoArrivalEvents.AtHospital;
    }

    // ------------------------------------------------------------------ helpers
    private Task<DriverAssignmentDto?> StepAsync(
        Guid driverUserId,
        AuditActor actor,
        string auditAction,
        CancellationToken cancellationToken,
        Action<AmbulanceAssignment, AmbulanceRequest> apply,
        Func<AmbulanceAssignment, Task>? afterCommit = null) =>
        StepCoreAsync(driverUserId, actor, auditAction, cancellationToken, (a, request) => { apply(a, request); return Task.FromResult(true); }, afterCommit);

    /// <summary>
    /// One driver step under the driver's gate. <paramref name="apply"/> returns false when there was nothing to change (a repeated
    /// tap): then nothing is saved, audited or broadcast, so retries never create duplicate history or duplicate real-time events.
    /// </summary>
    private Task<DriverAssignmentDto?> StepIfChangedAsync(
        Guid driverUserId,
        AuditActor actor,
        string auditAction,
        CancellationToken cancellationToken,
        Func<AmbulanceAssignment, AmbulanceRequest, bool> apply,
        Func<AmbulanceAssignment, Task>? afterCommit = null) =>
        StepCoreAsync(driverUserId, actor, auditAction, cancellationToken, (a, request) => Task.FromResult(apply(a, request)), afterCommit);

    private async Task<DriverAssignmentDto?> StepCoreAsync(
        Guid driverUserId,
        AuditActor actor,
        string auditAction,
        CancellationToken cancellationToken,
        Func<AmbulanceAssignment, AmbulanceRequest, Task<bool>> apply,
        Func<AmbulanceAssignment, Task>? afterCommit = null)
    {
        using var driverLock = await LockDriverAsync(driverUserId, cancellationToken);
        var assignment = await LoadAsync(driverUserId, tracking: true, cancellationToken)
            ?? throw new NotFoundException("You have no active assignment.");

        if (!await apply(assignment, assignment.Request))
            return assignment.IsActive ? ToDto(assignment) : null;
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

    private DriverAssignmentDto ToDto(AmbulanceAssignment a)
    {
        var options = Options;
        var hospitalKnown = options.HasHospitalLocation;
        return new DriverAssignmentDto(
            a.Id, a.Status, a.Request.RequestNumber, a.Request.Status, a.Request.Latitude, a.Request.Longitude, a.Request.Address,
            a.Request.EmergencyType, a.Request.InjuredCount, a.Request.RequesterName, a.Request.ContactPhone, a.Request.Description,
            a.Ambulance.VehicleNumber, a.Ambulance.Type, a.AssignedAt, Now(), AmbulanceRules.IsTripActive(a.Status),
            options.HospitalDisplayName,
            hospitalKnown ? options.HospitalLatitude : null,
            hospitalKnown ? options.HospitalLongitude : null,
            options.Enabled,
            options.Enabled && hospitalKnown,
            options.EffectiveMaxAccuracyMeters,
            a.ArrivalMethod,
            a.HospitalArrivedAt,
            a.PickupConfirmedSource,
            a.PickupConfirmedByName,
            a.HospitalArrivalMethod);
    }
}

/// <summary>The values of <see cref="DriverLocationResultDto.ArrivalEvent"/>.</summary>
public static class AutoArrivalEvents
{
    public const string AtPickup = "ArrivedAtPickup";
    public const string AtHospital = "ArrivedAtHospital";
}
