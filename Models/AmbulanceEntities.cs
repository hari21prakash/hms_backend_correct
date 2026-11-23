using HospitalManagement.Api.Models.Enums;

namespace HospitalManagement.Api.Models;

/// <summary>An external/private ambulance organisation. Its vehicles can only be dispatched while Status is Approved.</summary>
public class AmbulanceProvider : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string ContactPerson { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string LicenseNumber { get; set; } = string.Empty;
    public string? Notes { get; set; }

    public ProviderStatus Status { get; set; } = ProviderStatus.Pending;
    public string? StatusReason { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public User? ReviewedByUser { get; set; }
    public DateTime? ReviewedAt { get; set; }

    public ICollection<Ambulance> Ambulances { get; set; } = new List<Ambulance>();
    public ICollection<AmbulanceProviderUser> Users { get; set; } = new List<AmbulanceProviderUser>();
}

/// <summary>Links a login to ONE provider (as its administrator or as one of its drivers). Provider-linked users are scoped to that provider.</summary>
public class AmbulanceProviderUser : BaseEntity
{
    public Guid ProviderId { get; set; }
    public AmbulanceProvider Provider { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public ProviderUserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Ambulance : BaseEntity
{
    /// <summary>Stored upper-case; unique across the whole fleet.</summary>
    public string VehicleNumber { get; set; } = string.Empty;
    public AmbulanceType Type { get; set; }
    public AmbulanceOwnerType OwnerType { get; set; }

    public Guid? ProviderId { get; set; }
    public AmbulanceProvider? Provider { get; set; }

    public string? Make { get; set; }
    public string? Model { get; set; }
    public int? Year { get; set; }
    public int Capacity { get; set; } = 1;
    public string? Notes { get; set; }

    public AmbulanceStatus Status { get; set; } = AmbulanceStatus.Available;
    public bool IsActive { get; set; } = true;

    /// <summary>The driver (a user with the driver role) who operates this ambulance. One driver per ambulance.</summary>
    public Guid? DriverUserId { get; set; }
    public User? DriverUser { get; set; }

    // Last known position (updated by the driver's browser while a trip is active). History lives in AmbulanceLocationHistory.
    public double? CurrentLatitude { get; set; }
    public double? CurrentLongitude { get; set; }
    public double? LocationAccuracyMeters { get; set; }
    public double? LocationSpeedKmh { get; set; }
    public DateTime? LastLocationAt { get; set; }

    public ICollection<AmbulanceAssignment> Assignments { get; set; } = new List<AmbulanceAssignment>();
}

/// <summary>A public emergency ambulance booking. PatientId stays null until staff register/link the patient.</summary>
public class AmbulanceRequest : BaseEntity
{
    public string RequestNumber { get; set; } = string.Empty;

    /// <summary>SHA-256 (hex) of the secret tracking token. The token itself is never stored.</summary>
    public string TrackingTokenHash { get; set; } = string.Empty;

    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Address { get; set; } = string.Empty;
    public AmbulanceEmergencyType EmergencyType { get; set; }
    public int InjuredCount { get; set; } = 1;
    public string? RequesterName { get; set; }
    public string? ContactPhone { get; set; }
    public string? Description { get; set; }

    public AmbulanceRequestStatus Status { get; set; } = AmbulanceRequestStatus.Requested;
    public string? StatusReason { get; set; }

    public Guid? PatientId { get; set; }
    public Patient? Patient { get; set; }
    public Guid? PatientLinkedByUserId { get; set; }
    public User? PatientLinkedByUser { get; set; }
    public DateTime? PatientLinkedAt { get; set; }

    public ICollection<AmbulanceAssignment> Assignments { get; set; } = new List<AmbulanceAssignment>();
    public ICollection<AmbulanceStatusHistory> StatusHistory { get; set; } = new List<AmbulanceStatusHistory>();
}

/// <summary>One ambulance sent to one request. History is kept forever; at most one row per request and per ambulance is IsActive.</summary>
public class AmbulanceAssignment : BaseEntity
{
    public Guid RequestId { get; set; }
    public AmbulanceRequest Request { get; set; } = null!;

    public Guid AmbulanceId { get; set; }
    public Ambulance Ambulance { get; set; } = null!;

    /// <summary>The driver at the time of assignment (snapshot, so history survives driver changes).</summary>
    public Guid DriverUserId { get; set; }
    public User DriverUser { get; set; } = null!;

    public AssignmentStatus Status { get; set; } = AssignmentStatus.Pending;

    /// <summary>True while the assignment is in progress. Backed by unique partial indexes: no double booking is possible.</summary>
    public bool IsActive { get; set; } = true;

    public Guid? AssignedByUserId { get; set; }
    public User? AssignedByUser { get; set; }

    public DateTime AssignedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? ArrivedAt { get; set; }
    /// <summary>"Auto" when the server's geofence check recorded the arrival at the pickup point, "Manual" when the driver did.</summary>
    public string? ArrivalMethod { get; set; }
    /// <summary>Where the ambulance was (last accepted GPS fix) when arrival at the pickup point was recorded. Null if unknown.</summary>
    public double? ArrivedLatitude { get; set; }
    public double? ArrivedLongitude { get; set; }
    public double? ArrivedAccuracyMeters { get; set; }
    /// <summary>When the ambulance arrived at the hospital (the request moves to AtHospital at the same moment).</summary>
    public DateTime? HospitalArrivedAt { get; set; }
    public string? HospitalArrivalMethod { get; set; }
    public double? HospitalArrivedLatitude { get; set; }
    public double? HospitalArrivedLongitude { get; set; }
    public double? HospitalArrivedAccuracyMeters { get; set; }
    /// <summary>Who confirmed the patient is on board: "Driver" or "Dispatcher". The first valid confirmation wins; later ones change nothing.</summary>
    public string? PickupConfirmedSource { get; set; }
    public Guid? PickupConfirmedByUserId { get; set; }
    /// <summary>The confirmer's name at the time (kept as written, like an audit entry, so a later rename or deletion does not change history).</summary>
    public string? PickupConfirmedByName { get; set; }
    /// <summary>Who confirmed the handover that completed the trip: "Driver" or "Dispatcher".</summary>
    public string? HandoverConfirmedSource { get; set; }
    public Guid? HandoverConfirmedByUserId { get; set; }
    public string? HandoverConfirmedByName { get; set; }
    public DateTime? PickedUpAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? DeclineReason { get; set; }

    public ICollection<AmbulanceLocationHistory> LocationHistory { get; set; } = new List<AmbulanceLocationHistory>();
}

public class AmbulanceLocationHistory : BaseEntity
{
    public Guid AssignmentId { get; set; }
    public AmbulanceAssignment Assignment { get; set; } = null!;

    public Guid AmbulanceId { get; set; }
    public Ambulance Ambulance { get; set; } = null!;

    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? AccuracyMeters { get; set; }
    public double? SpeedKmh { get; set; }
    public double? Heading { get; set; }

    /// <summary>Server clock when the position was received (never the device clock).</summary>
    public DateTime ReceivedAt { get; set; }
}

/// <summary>Audit trail of status changes for a request and/or an ambulance, with the acting user and the server timestamp.</summary>
public class AmbulanceStatusHistory : BaseEntity
{
    public StatusHistoryScope Scope { get; set; }

    public Guid? RequestId { get; set; }
    public AmbulanceRequest? Request { get; set; }

    public Guid? AmbulanceId { get; set; }
    public Ambulance? Ambulance { get; set; }

    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;

    /// <summary>Null for the public booking itself and for system actions.</summary>
    public Guid? ChangedByUserId { get; set; }
    public User? ChangedByUser { get; set; }

    public DateTime ChangedAt { get; set; }
    public string? Note { get; set; }
}
