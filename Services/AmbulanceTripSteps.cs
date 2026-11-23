using HospitalManagement.Api.Data;
using HospitalManagement.Api.Exceptions;
using HospitalManagement.Api.Models;
using HospitalManagement.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagement.Api.Services;

/// <summary>
/// The ONE implementation of the trip steps that more than one party can trigger. The driver's buttons, the server's GPS detection and
/// the dispatch desk all call these, so every route follows the same rules, writes the same history and records who did it.
/// They change tracked entities and stage history rows; the caller holds <see cref="AmbulanceGates"/> and saves.
/// </summary>
public static class AmbulanceTripSteps
{
    /// <summary>The user's display name as it should be kept on the record (audit style: a snapshot, not a live link).</summary>
    public static async Task<string?> DisplayNameAsync(AppDbContext dbContext, Guid userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.FirstName, u.LastName, u.Email }).SingleOrDefaultAsync(cancellationToken);
        if (user is null) return null;
        var name = $"{user.FirstName} {user.LastName}".Trim();
        return AmbulanceRules.Trim(name.Length > 0 ? name : user.Email, 150);
    }

    /// <summary>The ambulance's last accepted GPS position, if it is recent enough to be meaningful; otherwise nothing is recorded.</summary>
    public static (double? Latitude, double? Longitude, double? AccuracyMeters) LastKnownPosition(Ambulance ambulance, DateTime now) =>
        ambulance.CurrentLatitude is { } latitude && ambulance.CurrentLongitude is { } longitude &&
        ambulance.LastLocationAt is { } at && now - at <= AmbulanceRules.MaxClientFixAge
            ? ((double?)latitude, (double?)longitude, ambulance.LocationAccuracyMeters)
            : ((double?)null, (double?)null, (double?)null);

    /// <summary>Stamps the hospital arrival on the assignment and moves the request to AtHospital (history row included).</summary>
    public static void RecordHospitalArrival(
        AppDbContext dbContext, AmbulanceAssignment assignment, AmbulanceRequest request, string method, DateTime at,
        (double? Latitude, double? Longitude, double? AccuracyMeters) position, Guid? actorUserId, string? note)
    {
        assignment.HospitalArrivedAt = at;
        assignment.HospitalArrivalMethod = method;
        (assignment.HospitalArrivedLatitude, assignment.HospitalArrivedLongitude, assignment.HospitalArrivedAccuracyMeters) = position;
        AmbulanceRules.MoveRequest(dbContext, request, AmbulanceRequestStatus.AtHospital, actorUserId ?? assignment.DriverUserId, note);
    }

    /// <summary>
    /// The patient is on board. GPS cannot prove that, so only a person confirms it: the driver, or an authorised member of staff.
    /// Allowed only once the ambulance has arrived at the scene.
    /// </summary>
    public static async Task ConfirmPickupAsync(
        AppDbContext dbContext, AmbulanceAssignment assignment, AmbulanceRequest request,
        string source, Guid userId, DateTime now, CancellationToken cancellationToken)
    {
        if (assignment.Status != AssignmentStatus.Arrived)
            throw new ConflictException($"The patient pick-up can be confirmed only after the ambulance has arrived at the scene (the assignment is {assignment.Status}).");

        var name = await DisplayNameAsync(dbContext, userId, cancellationToken);
        assignment.Status = AssignmentStatus.PickedUp;
        assignment.PickedUpAt = now;
        assignment.PickupConfirmedSource = source;
        assignment.PickupConfirmedByUserId = userId;
        assignment.PickupConfirmedByName = name;
        AmbulanceRules.MoveRequest(dbContext, request, AmbulanceRequestStatus.PatientPickedUp, userId, ConfirmationNote("Pick-up", source, name));
        AmbulanceRules.MoveAmbulance(dbContext, assignment.Ambulance, AmbulanceStatus.OnTrip, userId);
    }

    /// <summary>
    /// The patient has been handed over: the trip is complete and the ambulance is released. Only a person confirms it. If neither GPS
    /// nor the fallback button had recorded the arrival at the hospital, the handover records it (as a manual arrival).
    /// </summary>
    public static async Task ConfirmHandoverAsync(
        AppDbContext dbContext, AmbulanceAssignment assignment, AmbulanceRequest request,
        string source, Guid userId, DateTime now, CancellationToken cancellationToken)
    {
        if (assignment.Status != AssignmentStatus.PickedUp)
            throw new ConflictException($"The handover can be confirmed only after the patient has been picked up (the assignment is {assignment.Status}).");

        var name = await DisplayNameAsync(dbContext, userId, cancellationToken);
        assignment.Status = AssignmentStatus.Completed;
        assignment.IsActive = false;                         // tracking stops: no active assignment means no GPS is accepted
        assignment.CompletedAt = assignment.EndedAt = now;
        assignment.HandoverConfirmedSource = source;
        assignment.HandoverConfirmedByUserId = userId;
        assignment.HandoverConfirmedByName = name;
        if (request.Status != AmbulanceRequestStatus.AtHospital)
            RecordHospitalArrival(dbContext, assignment, request, AmbulanceRules.ArrivalManual, now,
                LastKnownPosition(assignment.Ambulance, now), userId, ConfirmationNote("Handover", source, name));
        AmbulanceRules.MoveAmbulance(dbContext, assignment.Ambulance, AmbulanceStatus.Available, userId, "Trip completed");
    }

    private static string ConfirmationNote(string what, string source, string? name) =>
        source == AmbulanceRules.SourceDispatcher
            ? $"{what} confirmed by dispatch{(name is null ? string.Empty : $" ({name})")}"
            : $"{what} confirmed by the driver";
}
