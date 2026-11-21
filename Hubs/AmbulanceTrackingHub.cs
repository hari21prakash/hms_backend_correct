using HospitalManagement.Api.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HospitalManagement.Api.Hubs;

/// <summary>
/// PUBLIC tracking hub (no login). A client can follow exactly ONE request, and only by proving it knows that request's
/// secret tracking token. There is deliberately no method that lists ambulances or subscribes to "everything".
/// Every failure returns the same generic message, so request numbers cannot be probed.
/// </summary>
[AllowAnonymous]
public class AmbulanceTrackingHub(IAmbulanceRequestService requests) : Hub
{
    public const string Path = "/hubs/ambulance-tracking";
    public const int MaxSubscribeAttemptsPerConnection = 8;
    public const string NotFoundMessage = "Tracking information was not found.";

    private const string GroupKey = "tracking-group";
    private const string AttemptsKey = "subscribe-attempts";

    public static string GroupFor(Guid requestId) => $"ambulance-track:{requestId:N}";

    /// <summary>Joins the group of the request identified by (requestNumber, token). Returns true on success.</summary>
    public async Task<bool> Subscribe(string requestNumber, string token)
    {
        var attempts = Context.Items.TryGetValue(AttemptsKey, out var value) && value is int count ? count : 0;
        if (attempts >= MaxSubscribeAttemptsPerConnection)
            throw new HubException("Too many attempts. Please reload the page.");
        Context.Items[AttemptsKey] = attempts + 1;

        var requestId = await requests.ValidateTrackingAccessAsync(requestNumber, token, Context.ConnectionAborted);
        if (requestId is null)
            throw new HubException(NotFoundMessage);

        await LeaveCurrentGroupAsync();
        var group = GroupFor(requestId.Value);
        await Groups.AddToGroupAsync(Context.ConnectionId, group, Context.ConnectionAborted);
        Context.Items[GroupKey] = group;
        return true;
    }

    public Task Unsubscribe() => LeaveCurrentGroupAsync();

    private async Task LeaveCurrentGroupAsync()
    {
        if (Context.Items.TryGetValue(GroupKey, out var existing) && existing is string group)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
            Context.Items.Remove(GroupKey);
        }
    }
}
