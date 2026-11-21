using HospitalManagement.Api.DTOs;
using HospitalManagement.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace HospitalManagement.Api.Services;

/// <summary>
/// Pushes ambulance events AFTER the database change is committed. Delivery is best-effort and can never fail the request;
/// clients also poll as a fallback. Public tracking clients receive ONLY the public-safe DTOs.
/// </summary>
public interface IAmbulanceBroadcaster
{
    Task RequestCreatedAsync(DispatchRequestDto request);
    Task RequestChangedAsync(DispatchRequestDto request, PublicTrackingDto publicView);
    Task AssignmentChangedAsync(DispatchAssignmentDto assignment);
    Task LocationChangedAsync(AmbulanceLocationEventDto dispatchEvent, PublicLocationUpdateDto publicUpdate, Guid requestId);
    Task AmbulanceAvailabilityChangedAsync(AmbulanceAvailabilityEventDto availability);
    Task AmbulanceArrivedAsync(Guid requestId, string requestNumber, DateTime arrivedAt);
}

public static class AmbulanceClientMethods
{
    // dispatch hub
    public const string RequestCreated = "requestCreated";
    public const string RequestUpdated = "requestUpdated";
    public const string AssignmentUpdated = "assignmentUpdated";
    public const string AmbulanceLocation = "ambulanceLocation";
    public const string AmbulanceAvailability = "ambulanceAvailability";
    // tracking hub
    public const string TrackingUpdated = "trackingUpdated";
    public const string LocationUpdated = "locationUpdated";
    public const string AmbulanceArrived = "ambulanceArrived";
}

public sealed class NullAmbulanceBroadcaster : IAmbulanceBroadcaster
{
    public Task RequestCreatedAsync(DispatchRequestDto request) => Task.CompletedTask;
    public Task RequestChangedAsync(DispatchRequestDto request, PublicTrackingDto publicView) => Task.CompletedTask;
    public Task AssignmentChangedAsync(DispatchAssignmentDto assignment) => Task.CompletedTask;
    public Task LocationChangedAsync(AmbulanceLocationEventDto dispatchEvent, PublicLocationUpdateDto publicUpdate, Guid requestId) => Task.CompletedTask;
    public Task AmbulanceAvailabilityChangedAsync(AmbulanceAvailabilityEventDto availability) => Task.CompletedTask;
    public Task AmbulanceArrivedAsync(Guid requestId, string requestNumber, DateTime arrivedAt) => Task.CompletedTask;
}

public sealed class SignalRAmbulanceBroadcaster(
    IHubContext<AmbulanceTrackingHub> tracking,
    IHubContext<AmbulanceDispatchHub> dispatch,
    ILogger<SignalRAmbulanceBroadcaster> logger) : IAmbulanceBroadcaster
{
    public Task RequestCreatedAsync(DispatchRequestDto request) =>
        Safe(() => dispatch.Clients.Group(AmbulanceDispatchHub.DispatchGroup).SendAsync(AmbulanceClientMethods.RequestCreated, request), nameof(RequestCreatedAsync));

    public async Task RequestChangedAsync(DispatchRequestDto request, PublicTrackingDto publicView)
    {
        await Safe(() => dispatch.Clients.Group(AmbulanceDispatchHub.DispatchGroup).SendAsync(AmbulanceClientMethods.RequestUpdated, request), nameof(RequestChangedAsync));
        await Safe(() => tracking.Clients.Group(AmbulanceTrackingHub.GroupFor(request.Id)).SendAsync(AmbulanceClientMethods.TrackingUpdated, publicView), nameof(RequestChangedAsync));
    }

    public Task AssignmentChangedAsync(DispatchAssignmentDto assignment) =>
        Safe(() => dispatch.Clients.Group(AmbulanceDispatchHub.DispatchGroup).SendAsync(AmbulanceClientMethods.AssignmentUpdated, assignment), nameof(AssignmentChangedAsync));

    public async Task LocationChangedAsync(AmbulanceLocationEventDto dispatchEvent, PublicLocationUpdateDto publicUpdate, Guid requestId)
    {
        // GPS goes only to staff with ambulances.tracking.read, and to the ONE public group of this request.
        await Safe(() => dispatch.Clients.Group(AmbulanceDispatchHub.TrackingGroup).SendAsync(AmbulanceClientMethods.AmbulanceLocation, dispatchEvent), nameof(LocationChangedAsync));
        await Safe(() => tracking.Clients.Group(AmbulanceTrackingHub.GroupFor(requestId)).SendAsync(AmbulanceClientMethods.LocationUpdated, publicUpdate), nameof(LocationChangedAsync));
    }

    public Task AmbulanceAvailabilityChangedAsync(AmbulanceAvailabilityEventDto availability) =>
        Safe(() => dispatch.Clients.Group(AmbulanceDispatchHub.DispatchGroup).SendAsync(AmbulanceClientMethods.AmbulanceAvailability, availability), nameof(AmbulanceAvailabilityChangedAsync));

    public Task AmbulanceArrivedAsync(Guid requestId, string requestNumber, DateTime arrivedAt) =>
        Safe(() => tracking.Clients.Group(AmbulanceTrackingHub.GroupFor(requestId)).SendAsync(AmbulanceClientMethods.AmbulanceArrived, new { requestNumber, arrivedAt }), nameof(AmbulanceArrivedAsync));

    private async Task Safe(Func<Task> send, string operation)
    {
        try { await send(); }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Ambulance real-time delivery failed ({Operation}); clients will catch up on their next poll", operation);
        }
    }
}
