using HospitalManagement.Api.DTOs;
using HospitalManagement.Api.Helpers;

namespace HospitalManagement.Api.Interfaces;

/// <summary>
/// Driver-side workflow and GPS ingestion. EVERY method takes the driver's user id from the authenticated JWT and only ever
/// touches that driver's own active assignment - there is no way to name another driver or assignment.
/// </summary>
public interface IAmbulanceTrackingService
{
    Task<DriverAssignmentDto?> GetMyAssignmentAsync(Guid driverUserId, CancellationToken cancellationToken);
    Task<DriverAssignmentDto?> AcceptAsync(Guid driverUserId, AuditActor actor, CancellationToken cancellationToken);
    Task<DriverAssignmentDto?> DeclineAsync(Guid driverUserId, string? reason, AuditActor actor, CancellationToken cancellationToken);
    Task<DriverAssignmentDto?> StartTripAsync(Guid driverUserId, AuditActor actor, CancellationToken cancellationToken);
    Task<DriverAssignmentDto?> ArrivedAsync(Guid driverUserId, AuditActor actor, CancellationToken cancellationToken);
    Task<DriverAssignmentDto?> PatientPickedUpAsync(Guid driverUserId, AuditActor actor, CancellationToken cancellationToken);
    Task<DriverAssignmentDto?> CompleteAsync(Guid driverUserId, AuditActor actor, CancellationToken cancellationToken);
    Task<DriverLocationResultDto> UpdateLocationAsync(Guid driverUserId, DriverLocationRequest request, CancellationToken cancellationToken);
}
