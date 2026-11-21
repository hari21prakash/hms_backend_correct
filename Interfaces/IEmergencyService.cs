using HospitalManagement.Api.DTOs;
using HospitalManagement.Api.Helpers;

namespace HospitalManagement.Api.Interfaces;

public interface IEmergencyService
{
    Task<EmergencyLookupDto> GetLookupAsync(CancellationToken cancellationToken);

    Task<EmergencyRegistrationResultDto> RegisterAsync(
        RegisterEmergencyPatientRequest request, AuditActor actor, CancellationToken cancellationToken);

    Task<EmergencyDashboardDto> GetDashboardAsync(
        Guid patientId, EmergencyViewAccess access, CancellationToken cancellationToken);

    Task<EmergencyAdmissionDto> AssignCareAsync(
        Guid patientId, AssignEmergencyCareRequest request, AuditActor actor, CancellationToken cancellationToken);

    Task<PatientListItemDto> CompleteDetailsAsync(
        Guid patientId, CompleteEmergencyPatientRequest request, AuditActor actor, CancellationToken cancellationToken);
}
