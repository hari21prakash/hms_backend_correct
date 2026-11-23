using System.ComponentModel.DataAnnotations;
using HospitalManagement.Api.Models.Enums;

namespace HospitalManagement.Api.DTOs;

// =====================================================================================================================
// Public booking and tracking. NOTHING in this section may contain driver identity, driver phone, other ambulances,
// or any patient information.
// =====================================================================================================================
public class CreateAmbulanceBookingRequest
{
    [Required] public double? Latitude { get; set; }
    [Required] public double? Longitude { get; set; }

    [Required, StringLength(500)]
    public string Address { get; set; } = string.Empty;

    public AmbulanceEmergencyType EmergencyType { get; set; } = AmbulanceEmergencyType.Accident;

    [Range(1, 100)]
    public int InjuredCount { get; set; } = 1;

    [StringLength(100)] public string? RequesterName { get; set; }
    [StringLength(30)] public string? ContactPhone { get; set; }
    [StringLength(1000)] public string? Description { get; set; }
}

/// <param name="TrackingPath">Relative URL. The secret token is in the URL FRAGMENT (#t=...), which browsers never send to servers or logs.</param>
public record PublicBookingResultDto(
    string RequestNumber,
    string TrackingToken,
    string TrackingPath,
    AmbulanceRequestStatus Status,
    DateTime CreatedAt);

public record PublicPointDto(double Latitude, double Longitude, string? Address);
public record PublicLocationDto(double Latitude, double Longitude, double? AccuracyMeters, double? SpeedKmh, DateTime UpdatedAt);
public record PublicTrailPointDto(double Latitude, double Longitude);
public record PublicTimelineItemDto(AmbulanceRequestStatus Status, DateTime At);

public record PublicTrackingDto(
    string RequestNumber,
    AmbulanceRequestStatus Status,
    string StatusLabel,
    DateTime CreatedAt,
    DateTime ServerTime,
    PublicPointDto Destination,
    string? AssignmentStatusLabel,
    string? VehicleNumber,
    AmbulanceType? AmbulanceType,
    bool IsTrackingActive,
    PublicLocationDto? Location,
    double? DistanceKm,
    int? EtaMinutes,
    IReadOnlyList<PublicTrailPointDto> Trail,
    IReadOnlyList<PublicTimelineItemDto> Timeline);

/// <summary>Pushed to a tracking group after every accepted GPS fix.</summary>
public record PublicLocationUpdateDto(PublicLocationDto Location, double? DistanceKm, int? EtaMinutes, DateTime ServerTime);

// =====================================================================================================================
// Staff: ambulances, drivers, providers
// =====================================================================================================================
public record AmbulanceLocationDto(double Latitude, double Longitude, double? AccuracyMeters, double? SpeedKmh, DateTime UpdatedAt);

public record AmbulanceDto(
    Guid Id,
    string VehicleNumber,
    AmbulanceType Type,
    AmbulanceOwnerType OwnerType,
    Guid? ProviderId,
    string? ProviderName,
    ProviderStatus? ProviderStatus,
    string? Make,
    string? Model,
    int? Year,
    int Capacity,
    string? Notes,
    AmbulanceStatus Status,
    bool IsActive,
    Guid? DriverUserId,
    string? DriverName,
    string? DriverPhone,
    AmbulanceLocationDto? Location,
    bool DispatchEligible,
    string? IneligibleReason,
    double? DistanceKm,
    Guid? ActiveRequestId,
    string? ActiveRequestNumber,
    AssignmentStatus? ActiveAssignmentStatus);

public class SaveAmbulanceRequest
{
    [Required, StringLength(30)] public string VehicleNumber { get; set; } = string.Empty;
    public AmbulanceType Type { get; set; } = AmbulanceType.BasicLifeSupport;
    [StringLength(100)] public string? Make { get; set; }
    [StringLength(100)] public string? Model { get; set; }
    [Range(1980, 2100)] public int? Year { get; set; }
    [Range(1, 20)] public int Capacity { get; set; } = 1;
    [StringLength(500)] public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Hospital staff only, on create: register the vehicle for this external provider. Provider admins always use their own provider.</summary>
    public Guid? ProviderId { get; set; }
}

public class SetAvailabilityRequest
{
    [Required] public AmbulanceStatus Status { get; set; }
    [StringLength(500)] public string? Note { get; set; }
}

public class SetAmbulanceDriverRequest
{
    public Guid? DriverUserId { get; set; }
}

public class AmbulanceQuery
{
    public AmbulanceStatus? Status { get; set; }
    public AmbulanceType? Type { get; set; }
    public AmbulanceOwnerType? OwnerType { get; set; }
    public Guid? ProviderId { get; set; }
    public string? Search { get; set; }
}

public record DriverOptionDto(
    Guid UserId,
    string FullName,
    string Email,
    string? Phone,
    Guid? ProviderId,
    string? ProviderName,
    Guid? AmbulanceId,
    string? VehicleNumber);

public class CreateAmbulanceDriverRequest
{
    [Required, StringLength(100)] public string FirstName { get; set; } = string.Empty;
    [Required, StringLength(100)] public string LastName { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = string.Empty;
    [StringLength(30)] public string? Phone { get; set; }
    [Required, StringLength(128, MinimumLength = 8)] public string Password { get; set; } = string.Empty;
}

public record ProviderDto(
    Guid Id,
    string Name,
    string ContactPerson,
    string Email,
    string Phone,
    string Address,
    string LicenseNumber,
    string? Notes,
    ProviderStatus Status,
    string? StatusReason,
    DateTime? ReviewedAt,
    string? ReviewedBy,
    DateTime CreatedAt,
    int AmbulanceCount,
    int AvailableAmbulanceCount,
    int DriverCount);

public class SaveProviderRequest
{
    [Required, StringLength(200)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(150)] public string ContactPerson { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = string.Empty;
    [Required, StringLength(30)] public string Phone { get; set; } = string.Empty;
    [Required, StringLength(300)] public string Address { get; set; } = string.Empty;
    [Required, StringLength(100)] public string LicenseNumber { get; set; } = string.Empty;
    [StringLength(1000)] public string? Notes { get; set; }

    // Create only: optionally create the provider's administrator login in the same step.
    [StringLength(100)] public string? AdminFirstName { get; set; }
    [StringLength(100)] public string? AdminLastName { get; set; }
    [EmailAddress, StringLength(256)] public string? AdminEmail { get; set; }
    [StringLength(128, MinimumLength = 8)] public string? AdminPassword { get; set; }
}

public class ProviderApprovalRequest
{
    [Required] public ProviderDecision Decision { get; set; }
    [StringLength(500)] public string? Reason { get; set; }
}

// =====================================================================================================================
// Dispatch
// =====================================================================================================================
public class DispatchRequestQuery
{
    /// <summary>true = only requests still in progress (not completed, cancelled or rejected).</summary>
    public bool? Open { get; set; }
    public AmbulanceRequestStatus? Status { get; set; }
}

public record StatusHistoryItemDto(string? FromStatus, string ToStatus, DateTime ChangedAt, string? ChangedBy, string? Note);

public record DispatchAssignmentDto(
    Guid Id,
    Guid RequestId,
    string RequestNumber,
    Guid AmbulanceId,
    string VehicleNumber,
    AmbulanceType AmbulanceType,
    AmbulanceOwnerType OwnerType,
    string? ProviderName,
    AssignmentStatus Status,
    bool IsActive,
    string? DriverName,
    string? DriverPhone,
    DateTime AssignedAt,
    DateTime? AcceptedAt,
    DateTime? StartedAt,
    DateTime? ArrivedAt,
    DateTime? PickedUpAt,
    DateTime? CompletedAt,
    string? DeclineReason,
    AmbulanceLocationDto? Location,
    double? DistanceKm,
    // ---- GPS-assisted workflow (optional so older callers keep working)
    /// <summary>"Auto" when GPS recorded the arrival at the scene, "Manual" when the driver did, null until it happened.</summary>
    string? ArrivalMethod = null,
    string? HospitalArrivalMethod = null,
    string? PickupConfirmedSource = null,
    /// <summary>The confirmation the trip is waiting for: "Pickup" (ambulance at the scene, patient not yet confirmed on board) or "Handover" (at the hospital, handover not yet confirmed).</summary>
    string? PendingConfirmation = null);

/// <summary>Who did what, and when, on one trip. Built from the latest assignment, so it is still there after the trip is completed.</summary>
public record TripConfirmationsDto(
    string? ArrivalMethod,
    DateTime? ArrivedAt,
    string? PickupSource,
    string? PickupBy,
    DateTime? PickedUpAt,
    string? HospitalArrivalMethod,
    DateTime? HospitalArrivedAt,
    string? HandoverSource,
    string? HandoverBy,
    DateTime? HandedOverAt);

public record DispatchRequestDto(
    Guid Id,
    string RequestNumber,
    AmbulanceRequestStatus Status,
    string? StatusReason,
    double Latitude,
    double Longitude,
    string Address,
    AmbulanceEmergencyType EmergencyType,
    int InjuredCount,
    string? RequesterName,
    string? ContactPhone,
    string? Description,
    DateTime CreatedAt,
    Guid? PatientId,
    string? PatientNumber,
    string? PatientName,
    DispatchAssignmentDto? ActiveAssignment,
    IReadOnlyList<StatusHistoryItemDto>? History,
    TripConfirmationsDto? Trip = null);

public class AssignAmbulanceRequest
{
    [Required] public Guid? AmbulanceId { get; set; }
}

public class ReasonRequest
{
    [StringLength(500)] public string? Reason { get; set; }
}

public class LinkPatientRequest
{
    [Required] public Guid? PatientId { get; set; }
}

/// <summary>What the caller may see. Locations need ambulances.tracking.read; driver identity needs fleet/dispatch access.</summary>
public record AmbulanceViewOptions(bool IncludeLocation, bool IncludeDriver);

public record AmbulanceLocationEventDto(Guid AmbulanceId, Guid RequestId, Guid AssignmentId, string VehicleNumber, AmbulanceLocationDto Location);
public record AmbulanceAvailabilityEventDto(Guid AmbulanceId, string VehicleNumber, AmbulanceStatus Status, bool IsActive);

// =====================================================================================================================
// Driver
// =====================================================================================================================
public record DriverAssignmentDto(
    Guid AssignmentId,
    AssignmentStatus Status,
    string RequestNumber,
    AmbulanceRequestStatus RequestStatus,
    double Latitude,
    double Longitude,
    string Address,
    AmbulanceEmergencyType EmergencyType,
    int InjuredCount,
    string? RequesterName,
    string? ContactPhone,
    string? Description,
    string VehicleNumber,
    AmbulanceType AmbulanceType,
    DateTime AssignedAt,
    DateTime ServerTime,
    bool TrackingRequired,
    // ---- GPS-assisted workflow (all optional so older callers keep working)
    /// <summary>The configured destination hospital, so the driver screen can show it and offer navigation. Null when not configured.</summary>
    string? HospitalName = null,
    double? HospitalLatitude = null,
    double? HospitalLongitude = null,
    /// <summary>True when the server will detect arrival at the pickup point from GPS. When false the driver uses the manual button.</summary>
    bool AutoPickupArrival = false,
    /// <summary>True when the server will detect arrival at the hospital from GPS (needs the hospital coordinates to be configured).</summary>
    bool AutoHospitalArrival = false,
    /// <summary>GPS fixes less accurate than this are ignored for arrival detection; the driver screen warns about them.</summary>
    double MaxAccuracyMeters = 50,
    string? ArrivalMethod = null,
    DateTime? HospitalArrivedAt = null,
    /// <summary>"Driver" or "Dispatcher": who confirmed the patient is on board (null until then).</summary>
    string? PickupConfirmedSource = null,
    string? PickupConfirmedBy = null,
    /// <summary>"Auto" (GPS) or "Manual": how the arrival at the hospital was recorded (null until it is).</summary>
    string? HospitalArrivalMethod = null);

public class DriverLocationRequest
{
    [Required] public double? Latitude { get; set; }
    [Required] public double? Longitude { get; set; }
    public double? AccuracyMeters { get; set; }
    public double? SpeedKmh { get; set; }
    public double? Heading { get; set; }
    /// <summary>Device clock; only used to discard obviously stale fixes. Never stored as the receipt time.</summary>
    public DateTime? ClientTimestamp { get; set; }
}

/// <param name="ArrivalEvent">"ArrivedAtPickup" or "ArrivedAtHospital" when THIS update caused the server to record an arrival; otherwise null.</param>
public record DriverLocationResultDto(bool Accepted, string? Reason, DateTime ServerTime, string? ArrivalEvent = null);
