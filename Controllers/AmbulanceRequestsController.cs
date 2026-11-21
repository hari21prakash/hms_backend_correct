using HospitalManagement.Api.DTOs;
using HospitalManagement.Api.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HospitalManagement.Api.Controllers;

/// <summary>
/// PUBLIC endpoints (no login): anyone at an accident can book an ambulance and follow it.
/// Both are rate limited per client IP. The tracking token is read from a HEADER, never from the query string.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/ambulance-requests")]
public class AmbulanceRequestsController(IAmbulanceRequestService requests) : ControllerBase
{
    public const string TrackingTokenHeader = "X-Tracking-Token";

    [HttpPost]
    [EnableRateLimiting("ambulance-booking")]
    public async Task<IActionResult> Create(CreateAmbulanceBookingRequest request, CancellationToken cancellationToken)
    {
        var result = await requests.CreateBookingAsync(request, cancellationToken);
        Response.Headers.CacheControl = "no-store";     // the response contains the secret token
        return StatusCode(StatusCodes.Status201Created, ApiResponse<PublicBookingResultDto>.Ok(result, "Ambulance request received."));
    }

    [HttpGet("track/{requestNumber}")]
    [EnableRateLimiting("ambulance-tracking")]
    public async Task<IActionResult> Track(string requestNumber, CancellationToken cancellationToken)
    {
        var token = Request.Headers[TrackingTokenHeader].FirstOrDefault();
        var result = await requests.GetPublicTrackingAsync(requestNumber, token, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return Ok(ApiResponse<PublicTrackingDto>.Ok(result));
    }
}
