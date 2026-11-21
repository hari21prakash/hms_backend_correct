using HospitalManagement.Api.Authorization;
using HospitalManagement.Api.DTOs;
using HospitalManagement.Api.Exceptions;
using HospitalManagement.Api.Helpers;
using HospitalManagement.Api.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HospitalManagement.Api.Controllers;

/// <summary>
/// Emergency patient registration. Every endpoint requires a signed-in staff member; nothing here is anonymous.
/// The acting user is always taken from the JWT, never from the request body.
/// </summary>
[ApiController]
[Authorize]
[Route("api/emergency")]
public class EmergencyController(IEmergencyService emergencyService) : ControllerBase
{
    private AuditActor Actor => AuditActor.FromPrincipal(User, HttpContext.Connection.RemoteIpAddress?.ToString());

    [HttpGet("lookup")]
    [HasPermission("emergency.manage")]
    public async Task<IActionResult> GetLookup(CancellationToken cancellationToken)
        => Ok(ApiResponse<EmergencyLookupDto>.Ok(await emergencyService.GetLookupAsync(cancellationToken)));

    [HttpPost("register")]
    [HasPermission("emergency.manage")]
    public async Task<IActionResult> Register(RegisterEmergencyPatientRequest request, CancellationToken cancellationToken)
    {
        var result = await emergencyService.RegisterAsync(request, Actor, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<EmergencyRegistrationResultDto>.Ok(result, "Emergency patient registered."));
    }

    /// <summary>
    /// Viewable by staff who can register emergencies or read patient records. Clinical sections are only included
    /// when the caller also holds the matching permission (prescriptions.read, laboratory.read, nursing.read).
    /// </summary>
    [HttpGet("{patientId:guid}")]
    public async Task<IActionResult> GetDashboard(Guid patientId, CancellationToken cancellationToken)
    {
        if (!Has("emergency.manage") && !Has("patients.read"))
            throw new ForbiddenException();

        var access = new EmergencyViewAccess(Has("prescriptions.read"), Has("laboratory.read"), Has("nursing.read"));
        return Ok(ApiResponse<EmergencyDashboardDto>.Ok(await emergencyService.GetDashboardAsync(patientId, access, cancellationToken)));
    }

    [HttpPost("{patientId:guid}/assignment")]
    [HasPermission("emergency.manage")]
    public async Task<IActionResult> AssignCare(Guid patientId, AssignEmergencyCareRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<EmergencyAdmissionDto>.Ok(
            await emergencyService.AssignCareAsync(patientId, request, Actor, cancellationToken), "Assignment saved."));

    [HttpPut("{patientId:guid}/identity")]
    [HasPermission("emergency.manage")]
    public async Task<IActionResult> CompleteDetails(Guid patientId, CompleteEmergencyPatientRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<PatientListItemDto>.Ok(
            await emergencyService.CompleteDetailsAsync(patientId, request, Actor, cancellationToken), "Patient details completed."));

    private bool Has(string permission) => User.HasClaim(PermissionAuthorizationHandler.ClaimType, permission);
}
