using HospitalManagement.Api.DTOs;

namespace HospitalManagement.Api.Interfaces;

/// <summary>Public (anonymous) booking and tracking.</summary>
public interface IAmbulanceRequestService
{
    Task<PublicBookingResultDto> CreateBookingAsync(CreateAmbulanceBookingRequest request, CancellationToken cancellationToken);

    /// <summary>Throws NotFoundException (with one generic message) for an unknown number AND for a wrong token.</summary>
    Task<PublicTrackingDto> GetPublicTrackingAsync(string requestNumber, string? token, CancellationToken cancellationToken);

    /// <summary>Returns the request id when (number, token) is valid; otherwise null.</summary>
    Task<Guid?> ValidateTrackingAccessAsync(string requestNumber, string? token, CancellationToken cancellationToken);

    /// <summary>The public-safe view of a request, used to push live updates.</summary>
    Task<PublicTrackingDto> BuildPublicViewAsync(Guid requestId, CancellationToken cancellationToken);
}
