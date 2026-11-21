using HospitalManagement.Api.Authorization;
using HospitalManagement.Api.DTOs;
using HospitalManagement.Api.Helpers;
using Microsoft.AspNetCore.Mvc;

namespace HospitalManagement.Api.Controllers;

/// <summary>Shared helpers: the acting user always comes from the JWT, permissions from its claims.</summary>
public abstract class AmbulanceControllerBase : ControllerBase
{
    protected bool Has(string permission) => User.HasClaim(PermissionAuthorizationHandler.ClaimType, permission);

    protected Guid UserId => User.GetRequiredUserId();

    protected AuditActor Actor => AuditActor.FromPrincipal(User, HttpContext.Connection.RemoteIpAddress?.ToString());

    /// <summary>GPS needs ambulances.tracking.read; driver names and phones need fleet/dispatch/request access.</summary>
    protected AmbulanceViewOptions View => new(
        IncludeLocation: Has("ambulances.tracking.read"),
        IncludeDriver: Has("ambulances.read") || Has("ambulances.dispatch") || Has("ambulances.manage") || Has("ambulances.requests.read"));
}
