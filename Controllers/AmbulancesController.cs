using HospitalManagement.Api.Authorization;
using HospitalManagement.Api.DTOs;
using HospitalManagement.Api.Interfaces;
using HospitalManagement.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HospitalManagement.Api.Controllers;

/// <summary>Fleet management. Provider administrators reach only their own provider's vehicles (enforced in the service).</summary>
[ApiController]
[Authorize]
[Route("api/ambulances")]
public class AmbulancesController(IAmbulanceService ambulances, AmbulanceCallerResolver callers) : AmbulanceControllerBase
{
    [HttpGet]
    [HasPermission("ambulances.read")]
    public async Task<IActionResult> List([FromQuery] AmbulanceQuery query, CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<AmbulanceDto>>.Ok(
            await ambulances.ListAsync(query, await callers.ResolveAsync(UserId, cancellationToken), View, cancellationToken)));

    [HttpGet("available")]
    [HasPermission("ambulances.read")]
    public async Task<IActionResult> Available([FromQuery] Guid? requestId, CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<AmbulanceDto>>.Ok(
            await ambulances.GetAvailableAsync(requestId, await callers.ResolveAsync(UserId, cancellationToken), View, cancellationToken)));

    [HttpGet("drivers")]
    [HasPermission("ambulances.manage")]
    public async Task<IActionResult> Drivers(CancellationToken cancellationToken)
        => Ok(ApiResponse<IReadOnlyList<DriverOptionDto>>.Ok(
            await ambulances.ListDriversAsync(await callers.ResolveAsync(UserId, cancellationToken), cancellationToken)));

    [HttpPost("drivers")]
    [HasPermission("ambulances.manage")]
    public async Task<IActionResult> CreateDriver(CreateAmbulanceDriverRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, ApiResponse<DriverOptionDto>.Ok(
            await ambulances.CreateHospitalDriverAsync(request, await callers.ResolveAsync(UserId, cancellationToken), Actor, cancellationToken), "Driver account created."));

    [HttpGet("{id:guid}")]
    [HasPermission("ambulances.read")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
        => Ok(ApiResponse<AmbulanceDto>.Ok(
            await ambulances.GetAsync(id, await callers.ResolveAsync(UserId, cancellationToken), View, cancellationToken)));

    [HttpPost]
    [HasPermission("ambulances.manage")]
    public async Task<IActionResult> Create(SaveAmbulanceRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, ApiResponse<AmbulanceDto>.Ok(
            await ambulances.CreateAsync(request, await callers.ResolveAsync(UserId, cancellationToken), Actor, View, cancellationToken), "Ambulance registered."));

    [HttpPut("{id:guid}")]
    [HasPermission("ambulances.manage")]
    public async Task<IActionResult> Update(Guid id, SaveAmbulanceRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<AmbulanceDto>.Ok(
            await ambulances.UpdateAsync(id, request, await callers.ResolveAsync(UserId, cancellationToken), Actor, View, cancellationToken), "Ambulance updated."));

    [HttpPatch("{id:guid}/availability")]
    [HasPermission("ambulances.manage")]
    public async Task<IActionResult> SetAvailability(Guid id, SetAvailabilityRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<AmbulanceDto>.Ok(
            await ambulances.SetAvailabilityAsync(id, request, await callers.ResolveAsync(UserId, cancellationToken), Actor, View, cancellationToken), "Availability updated."));

    [HttpPatch("{id:guid}/driver")]
    [HasPermission("ambulances.manage")]
    public async Task<IActionResult> SetDriver(Guid id, SetAmbulanceDriverRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<AmbulanceDto>.Ok(
            await ambulances.SetDriverAsync(id, request, await callers.ResolveAsync(UserId, cancellationToken), Actor, View, cancellationToken), "Driver updated."));
}
