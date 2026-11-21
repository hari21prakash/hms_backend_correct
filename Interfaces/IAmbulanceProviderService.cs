using HospitalManagement.Api.DTOs;
using HospitalManagement.Api.Helpers;
using HospitalManagement.Api.Services;

namespace HospitalManagement.Api.Interfaces;

public interface IAmbulanceProviderService
{
    /// <param name="canManageProviders">Holds ambulances.providers.manage (hospital administrators).</param>
    Task<IReadOnlyList<ProviderDto>> ListAsync(AmbulanceCaller caller, bool canManageProviders, CancellationToken cancellationToken);
    Task<ProviderDto> GetAsync(Guid id, AmbulanceCaller caller, bool canManageProviders, CancellationToken cancellationToken);
    Task<ProviderDto> CreateAsync(SaveProviderRequest request, AmbulanceCaller caller, AuditActor actor, CancellationToken cancellationToken);
    Task<ProviderDto> UpdateAsync(Guid id, SaveProviderRequest request, AmbulanceCaller caller, bool canManageProviders, AuditActor actor, CancellationToken cancellationToken);
    Task<ProviderDto> SetApprovalAsync(Guid id, ProviderApprovalRequest request, AmbulanceCaller caller, AuditActor actor, CancellationToken cancellationToken);
    Task<IReadOnlyList<DriverOptionDto>> ListDriversAsync(Guid providerId, AmbulanceCaller caller, bool canManageProviders, CancellationToken cancellationToken);
    Task<DriverOptionDto> CreateDriverAsync(Guid providerId, CreateAmbulanceDriverRequest request, AmbulanceCaller caller, bool canManageProviders, AuditActor actor, CancellationToken cancellationToken);
}
