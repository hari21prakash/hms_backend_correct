using HospitalManagement.Api.DTOs;
using HospitalManagement.Api.Helpers;

namespace HospitalManagement.Api.Interfaces;

/// <summary>Hospital dispatch desk. Callers must be hospital staff (never provider-linked users).</summary>
public interface IAmbulanceDispatchService
{
    Task<IReadOnlyList<DispatchRequestDto>> ListRequestsAsync(DispatchRequestQuery query, AmbulanceViewOptions view, CancellationToken cancellationToken);
    Task<DispatchRequestDto> GetRequestAsync(Guid id, AmbulanceViewOptions view, CancellationToken cancellationToken);
    Task<DispatchRequestDto> AcceptAsync(Guid id, AuditActor actor, AmbulanceViewOptions view, CancellationToken cancellationToken);
    Task<DispatchRequestDto> RejectAsync(Guid id, string? reason, AuditActor actor, AmbulanceViewOptions view, CancellationToken cancellationToken);
    Task<DispatchRequestDto> CancelAsync(Guid id, string? reason, AuditActor actor, AmbulanceViewOptions view, CancellationToken cancellationToken);
    Task<DispatchRequestDto> AssignAsync(Guid id, Guid ambulanceId, AuditActor actor, AmbulanceViewOptions view, CancellationToken cancellationToken);
    /// <summary>The patient is on board (the ambulance must have arrived). The first valid confirmation, from the driver or from here, wins.</summary>
    Task<DispatchRequestDto> ConfirmPickupAsync(Guid id, AuditActor actor, AmbulanceViewOptions view, CancellationToken cancellationToken);
    /// <summary>The patient has been handed over: completes the trip and releases the ambulance. The first valid confirmation wins.</summary>
    Task<DispatchRequestDto> ConfirmHandoverAsync(Guid id, AuditActor actor, AmbulanceViewOptions view, CancellationToken cancellationToken);
    Task<DispatchRequestDto> CompleteAsync(Guid id, AuditActor actor, AmbulanceViewOptions view, CancellationToken cancellationToken);
    Task<DispatchRequestDto> LinkPatientAsync(Guid id, Guid patientId, AuditActor actor, AmbulanceViewOptions view, CancellationToken cancellationToken);
    Task<IReadOnlyList<DispatchAssignmentDto>> GetActiveAsync(AmbulanceViewOptions view, CancellationToken cancellationToken);
    Task<IReadOnlyList<AmbulanceDto>> GetFleetAsync(AmbulanceViewOptions view, CancellationToken cancellationToken);
}
