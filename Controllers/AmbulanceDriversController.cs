using HospitalManagement.Api.Authorization;
using HospitalManagement.Api.DTOs;
using HospitalManagement.Api.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HospitalManagement.Api.Controllers;

/// <summary>
/// Driver mobile website API. No endpoint accepts a driver, user or assignment id: the service always works on the
/// authenticated driver's own active assignment.
/// </summary>
[ApiController]
[Authorize]
[Route("api/ambulance-drivers")]
public class AmbulanceDriversController(IAmbulanceTrackingService tracking) : AmbulanceControllerBase
{
    [HttpGet("my-assignment")]
    [HasPermission("ambulances.driver.status")]
    public async Task<IActionResult> MyAssignment(CancellationToken cancellationToken)
        => Ok(ApiResponse<DriverAssignmentDto?>.Ok(await tracking.GetMyAssignmentAsync(UserId, cancellationToken)));

    [HttpPost("location")]
    [HasPermission("ambulances.driver.location")]
    public async Task<IActionResult> Location(DriverLocationRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<DriverLocationResultDto>.Ok(await tracking.UpdateLocationAsync(UserId, request, cancellationToken, Actor)));

    [HttpPost("accept")]
    [HasPermission("ambulances.driver.status")]
    public async Task<IActionResult> Accept(CancellationToken cancellationToken)
        => Ok(ApiResponse<DriverAssignmentDto?>.Ok(await tracking.AcceptAndStartAsync(UserId, Actor, cancellationToken), "Assignment accepted. Tracking started."));

    [HttpPost("decline")]
    [HasPermission("ambulances.driver.status")]
    public async Task<IActionResult> Decline(ReasonRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse<DriverAssignmentDto?>.Ok(await tracking.DeclineAsync(UserId, request.Reason, Actor, cancellationToken), "Assignment declined."));

    [HttpPost("start-trip")]
    [HasPermission("ambulances.driver.status")]
    public async Task<IActionResult> StartTrip(CancellationToken cancellationToken)
        => Ok(ApiResponse<DriverAssignmentDto?>.Ok(await tracking.StartTripAsync(UserId, Actor, cancellationToken), "Trip started."));

    [HttpPost("arrived")]
    [HasPermission("ambulances.driver.status")]
    public async Task<IActionResult> Arrived(CancellationToken cancellationToken)
        => Ok(ApiResponse<DriverAssignmentDto?>.Ok(await tracking.ArrivedAsync(UserId, Actor, cancellationToken), "Arrival recorded."));

    /// <summary>Fallback only: the server normally records the hospital arrival itself from GPS.</summary>
    [HttpPost("hospital-arrived")]
    [HasPermission("ambulances.driver.status")]
    public async Task<IActionResult> HospitalArrived(CancellationToken cancellationToken)
        => Ok(ApiResponse<DriverAssignmentDto?>.Ok(await tracking.HospitalArrivedAsync(UserId, Actor, cancellationToken), "Hospital arrival recorded."));

    [HttpPost("patient-picked-up")]
    [HasPermission("ambulances.driver.status")]
    public async Task<IActionResult> PatientPickedUp(CancellationToken cancellationToken)
        => Ok(ApiResponse<DriverAssignmentDto?>.Ok(await tracking.PatientPickedUpAsync(UserId, Actor, cancellationToken), "Patient pick-up recorded."));

    [HttpPost("complete")]
    [HasPermission("ambulances.driver.status")]
    public async Task<IActionResult> Complete(CancellationToken cancellationToken)
        => Ok(ApiResponse<DriverAssignmentDto?>.Ok(await tracking.CompleteAsync(UserId, Actor, cancellationToken), "Trip completed."));
}
