using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace HospitalManagement.Api.Helpers;

/// <summary>Who performed an action, captured from the authenticated request (never from the request body).</summary>
public sealed record AuditActor(Guid? UserId, string? Email, string? Role, string? IpAddress)
{
    public static AuditActor FromPrincipal(ClaimsPrincipal principal, string? ipAddress) => new(
        principal.GetRequiredUserId(),
        principal.FindFirstValue(JwtRegisteredClaimNames.Email),
        principal.FindFirstValue(ClaimTypes.Role),
        ipAddress);
}
