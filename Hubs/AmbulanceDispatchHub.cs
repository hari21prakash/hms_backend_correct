using HospitalManagement.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HospitalManagement.Api.Hubs;

/// <summary>
/// Staff-only hub. The connection must be authenticated AND hold a dispatch permission:
///  * ambulances.dispatch or ambulances.requests.read  -> group "dispatch" (requests, assignments, availability)
///  * ambulances.tracking.read (in addition)            -> group "dispatch-tracking" (live GPS)
/// Provider administrators hold neither dispatch permission, so they are refused and can never receive hospital-wide GPS.
/// </summary>
[Authorize]
public class AmbulanceDispatchHub : Hub
{
    public const string Path = "/hubs/ambulance-dispatch";
    public const string DispatchGroup = "ambulance-dispatch";
    public const string TrackingGroup = "ambulance-dispatch-tracking";

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        var canSeeRequests = Has(user, "ambulances.dispatch") || Has(user, "ambulances.requests.read");
        if (!canSeeRequests)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, DispatchGroup);
        if (Has(user, "ambulances.tracking.read"))
            await Groups.AddToGroupAsync(Context.ConnectionId, TrackingGroup);

        await base.OnConnectedAsync();
    }

    private static bool Has(System.Security.Claims.ClaimsPrincipal? user, string permission) =>
        user?.HasClaim(PermissionAuthorizationHandler.ClaimType, permission) == true;
}
