using HospitalManagement.Api.Authorization;
using HospitalManagement.Api.DTOs;
using HospitalManagement.Api.Interfaces;
using HospitalManagement.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HospitalManagement.Api.Controllers;

/// <summary>Hospital dispatch desk. Hospital staff only (provider-linked users are refused even if they somehow hold a permission).</summary>
[ApiController]
[Authorize]
[Route("api/ambulance-dispatch")]
public class AmbulanceDispatchController(IAmbulanceDispatchService dispatch, AmbulanceCallerResolver callers) : AmbulanceControllerBase
{
    private Task Guard(CancellationToken cancellationToken) => callers.RequireHospitalStaffAsync(UserId, cancellationToken);

    [HttpGet("requests")]
    [HasPermission("ambulances.requests.read")]
    public async Task<IActionResult> Requests([FromQuery] DispatchRequestQuery query, CancellationToken cancellationToken)
    {
        await Guard(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<DispatchRequestDto>>.Ok(await dispatch.ListRequestsAsync(query, View, cancellationToken)));
    }

    [HttpGet("requests/{id:guid}")]
    [HasPermission("ambulances.requests.read")]
    public async Task<IActionResult> GetRequest(Guid id, CancellationToken cancellationToken)
    {
        await Guard(cancellationToken);
        return Ok(ApiResponse<DispatchRequestDto>.Ok(await dispatch.GetRequestAsync(id, View, cancellationToken)));
    }

    [HttpPost("requests/{id:guid}/accept")]
    [HasPermission("ambulances.requests.manage")]
    public async Task<IActionResult> Accept(Guid id, CancellationToken cancellationToken)
    {
        await Guard(cancellationToken);
        return Ok(ApiResponse<DispatchRequestDto>.Ok(await dispatch.AcceptAsync(id, Actor, View, cancellationToken), "Request accepted."));
    }

    [HttpPost("requests/{id:guid}/reject")]
    [HasPermission("ambulances.requests.manage")]
    public async Task<IActionResult> Reject(Guid id, ReasonRequest request, CancellationToken cancellationToken)
    {
        await Guard(cancellationToken);
        return Ok(ApiResponse<DispatchRequestDto>.Ok(await dispatch.RejectAsync(id, request.Reason, Actor, View, cancellationToken), "Request rejected."));
    }

    [HttpPost("requests/{id:guid}/cancel")]
    [HasPermission("ambulances.requests.manage")]
    public async Task<IActionResult> Cancel(Guid id, ReasonRequest request, CancellationToken cancellationToken)
    {
        await Guard(cancellationToken);
        return Ok(ApiResponse<DispatchRequestDto>.Ok(await dispatch.CancelAsync(id, request.Reason, Actor, View, cancellationToken), "Request cancelled."));
    }

    [HttpPost("requests/{id:guid}/assign")]
    [HasPermission("ambulances.dispatch")]
    public async Task<IActionResult> Assign(Guid id, AssignAmbulanceRequest request, CancellationToken cancellationToken)
    {
        await Guard(cancellationToken);
        return Ok(ApiResponse<DispatchRequestDto>.Ok(await dispatch.AssignAsync(id, request.AmbulanceId!.Value, Actor, View, cancellationToken), "Ambulance assigned."));
    }

    /// <summary>Hospital/dispatch confirmation that the patient is on board. The driver's own button remains as the fallback.</summary>
    [HttpPost("requests/{id:guid}/confirm-pickup")]
    [HasPermission("ambulances.requests.manage")]
    public async Task<IActionResult> ConfirmPickup(Guid id, CancellationToken cancellationToken)
    {
        await Guard(cancellationToken);
        return Ok(ApiResponse<DispatchRequestDto>.Ok(await dispatch.ConfirmPickupAsync(id, Actor, View, cancellationToken), "Patient pick-up confirmed."));
    }

    /// <summary>Hospital/dispatch confirmation that the patient was handed over. Completes the trip and releases the ambulance.</summary>
    [HttpPost("requests/{id:guid}/confirm-handover")]
    [HasPermission("ambulances.requests.manage")]
    public async Task<IActionResult> ConfirmHandover(Guid id, CancellationToken cancellationToken)
    {
        await Guard(cancellationToken);
        return Ok(ApiResponse<DispatchRequestDto>.Ok(await dispatch.ConfirmHandoverAsync(id, Actor, View, cancellationToken), "Patient handover confirmed. Trip completed."));
    }

    [HttpPost("requests/{id:guid}/complete")]
    [HasPermission("ambulances.requests.manage")]
    public async Task<IActionResult> Complete(Guid id, CancellationToken cancellationToken)
    {
        await Guard(cancellationToken);
        return Ok(ApiResponse<DispatchRequestDto>.Ok(await dispatch.CompleteAsync(id, Actor, View, cancellationToken), "Request completed."));
    }

    /// <summary>Links an EXISTING patient (for example one just created through Emergency Registration) to the request.</summary>
    [HttpPost("requests/{id:guid}/link-patient")]
    [HasPermission("ambulances.requests.manage")]
    public async Task<IActionResult> LinkPatient(Guid id, LinkPatientRequest request, CancellationToken cancellationToken)
    {
        await Guard(cancellationToken);
        return Ok(ApiResponse<DispatchRequestDto>.Ok(await dispatch.LinkPatientAsync(id, request.PatientId!.Value, Actor, View, cancellationToken), "Patient linked."));
    }

    [HttpGet("active")]
    [HasPermission("ambulances.dispatch")]
    public async Task<IActionResult> Active(CancellationToken cancellationToken)
    {
        await Guard(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<DispatchAssignmentDto>>.Ok(await dispatch.GetActiveAsync(View, cancellationToken)));
    }

    [HttpGet("fleet")]
    [HasPermission("ambulances.dispatch")]
    public async Task<IActionResult> Fleet(CancellationToken cancellationToken)
    {
        await Guard(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<AmbulanceDto>>.Ok(await dispatch.GetFleetAsync(View, cancellationToken)));
    }
}
