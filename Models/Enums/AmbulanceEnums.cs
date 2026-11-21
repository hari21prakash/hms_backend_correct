namespace HospitalManagement.Api.Models.Enums;

public enum AmbulanceType { BasicLifeSupport, AdvancedLifeSupport, IcuAmbulance, NeonatalAmbulance }

/// <summary>Hospital = owned by this hospital (no provider). External = belongs to an approved external provider.</summary>
public enum AmbulanceOwnerType { Hospital, External }

/// <summary>
/// Available / OutOfService are set by staff. Reserved / Dispatched / OnTrip are driven only by the dispatch workflow.
/// </summary>
public enum AmbulanceStatus { Available, Reserved, Dispatched, OnTrip, OutOfService }

public enum ProviderStatus { Pending, Approved, Rejected, Suspended }

public enum ProviderUserRole { ProviderAdmin, Driver }

/// <summary>Requested -> Accepted -> Assigned -> Dispatched -> Arrived -> PatientPickedUp -> AtHospital -> Completed (or Cancelled / Rejected).</summary>
public enum AmbulanceRequestStatus
{
    Requested, Accepted, Assigned, Dispatched, Arrived, PatientPickedUp, AtHospital, Completed, Cancelled, Rejected
}

public enum AmbulanceEmergencyType { Accident, MedicalEmergency, CardiacEmergency, BreathingDifficulty, Burns, Other }

/// <summary>Pending = waiting for the driver. EnRoute = trip started (GPS tracking is live from here until the assignment ends).</summary>
public enum AssignmentStatus { Pending, Accepted, Declined, EnRoute, Arrived, PickedUp, Completed, Cancelled }

public enum StatusHistoryScope { Request, Ambulance }

/// <summary>Staff decision on a provider registration (not stored; the result is ProviderStatus).</summary>
public enum ProviderDecision { Approve, Reject, Suspend, Reinstate }
