using HospitalManagement.Api.DTOs;
using HospitalManagement.Api.Helpers;
using HospitalManagement.Api.Services;

namespace HospitalManagement.Api.Interfaces;

public interface IAmbulanceService
{
    Task<IReadOnlyList<AmbulanceDto>> ListAsync(AmbulanceQuery query, AmbulanceCaller caller, AmbulanceViewOptions view, CancellationToken cancellationToken);
    Task<AmbulanceDto> GetAsync(Guid id, AmbulanceCaller caller, AmbulanceViewOptions view, CancellationToken cancellationToken);
    Task<AmbulanceDto> CreateAsync(SaveAmbulanceRequest request, AmbulanceCaller caller, AuditActor actor, AmbulanceViewOptions view, CancellationToken cancellationToken);
    Task<AmbulanceDto> UpdateAsync(Guid id, SaveAmbulanceRequest request, AmbulanceCaller caller, AuditActor actor, AmbulanceViewOptions view, CancellationToken cancellationToken);
    Task<AmbulanceDto> SetAvailabilityAsync(Guid id, SetAvailabilityRequest request, AmbulanceCaller caller, AuditActor actor, AmbulanceViewOptions view, CancellationToken cancellationToken);
    Task<AmbulanceDto> SetDriverAsync(Guid id, SetAmbulanceDriverRequest request, AmbulanceCaller caller, AuditActor actor, AmbulanceViewOptions view, CancellationToken cancellationToken);

    /// <summary>Ambulances that can be dispatched right now (available, active, driver assigned, provider approved), nearest first when a point is given.</summary>
    Task<IReadOnlyList<AmbulanceDto>> GetAvailableAsync(Guid? requestId, AmbulanceCaller caller, AmbulanceViewOptions view, CancellationToken cancellationToken);

    Task<IReadOnlyList<DriverOptionDto>> ListDriversAsync(AmbulanceCaller caller, CancellationToken cancellationToken);
    Task<DriverOptionDto> CreateHospitalDriverAsync(CreateAmbulanceDriverRequest request, AmbulanceCaller caller, AuditActor actor, CancellationToken cancellationToken);
}
