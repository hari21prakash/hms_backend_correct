using HospitalManagement.Api.Authorization;
using HospitalManagement.Api.DTOs;
using HospitalManagement.Api.Exceptions;
using HospitalManagement.Api.Interfaces;
using HospitalManagement.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HospitalManagement.Api.Controllers;

/// <summary>
/// External provider registry. Hospital administrators hold ambulances.providers.manage. Provider administrators hold only
/// ambulances.manage and are confined to their own provider by the service. Approval needs providers.manage, which a provider
/// administrator never has, so a provider can never approve itself.
/// </summary>
[ApiController]
[Authorize]
[Route("api/ambulance-providers")]
public class AmbulanceProvidersController(IAmbulanceProviderService providers, AmbulanceCallerResolver callers) : AmbulanceControllerBase
{
    private bool CanManageProviders => Has("ambulances.providers.manage");

    private void RequireProviderAccess()
    {
        if (!CanManageProviders && !Has("ambulances.manage"))
            throw new ForbiddenException("You do not have permission to manage ambulance providers.");
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        RequireProviderAccess();
        return Ok(ApiResponse<IReadOnlyList<ProviderDto>>.Ok(
            await providers.ListAsync(await callers.ResolveAsync(UserId, cancellationToken), CanManageProviders, cancellationToken)));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        RequireProviderAccess();
        return Ok(ApiResponse<ProviderDto>.Ok(
            await providers.GetAsync(id, await callers.ResolveAsync(UserId, cancellationToken), CanManageProviders, cancellationToken)));
    }

    [HttpPost]
    [HasPermission("ambulances.providers.manage")]
    public async Task<IActionResult> Create(SaveProviderRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, ApiResponse<ProviderDto>.Ok(
            await providers.CreateAsync(request, await callers.ResolveAsync(UserId, cancellationToken), Actor, cancellationToken), "Provider registered. It must be approved before its ambulances can be dispatched."));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SaveProviderRequest request, CancellationToken cancellationToken)
    {
        RequireProviderAccess();
        return Ok(ApiResponse<ProviderDto>.Ok(
            await providers.UpdateAsync(id, request, await callers.ResolveAsync(UserId, cancellationToken), CanManageProviders, Actor, cancellationToken), "Provider updated."));
    }

    [HttpPatch("{id:guid}/approval")]
    [HasPermission("ambulances.providers.manage")]
    public async Task<IActionResult> SetApproval(Guid id, ProviderApprovalRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<ProviderDto>.Ok(
            await providers.SetApprovalAsync(id, request, await callers.ResolveAsync(UserId, cancellationToken), Actor, cancellationToken), "Provider status updated."));

    [HttpGet("{id:guid}/drivers")]
    public async Task<IActionResult> Drivers(Guid id, CancellationToken cancellationToken)
    {
        RequireProviderAccess();
        return Ok(ApiResponse<IReadOnlyList<DriverOptionDto>>.Ok(
            await providers.ListDriversAsync(id, await callers.ResolveAsync(UserId, cancellationToken), CanManageProviders, cancellationToken)));
    }

    [HttpPost("{id:guid}/drivers")]
    public async Task<IActionResult> CreateDriver(Guid id, CreateAmbulanceDriverRequest request, CancellationToken cancellationToken)
    {
        RequireProviderAccess();
        return StatusCode(StatusCodes.Status201Created, ApiResponse<DriverOptionDto>.Ok(
            await providers.CreateDriverAsync(id, request, await callers.ResolveAsync(UserId, cancellationToken), CanManageProviders, Actor, cancellationToken), "Driver account created."));
    }
}
