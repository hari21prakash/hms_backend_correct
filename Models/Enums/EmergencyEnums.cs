namespace HospitalManagement.Api.Models.Enums;

public enum EmergencyType { Accident, UnconsciousPatient, CardiacEmergency, BreathingDifficulty, Other }

/// <summary>
/// Unidentified = nothing known about the person. IdentifiedIncomplete = a name is known but the registration details
/// (date of birth, phone, address, contact) are still missing. Identified = full details were completed.
/// Normal registrations are always Identified.
/// </summary>
public enum IdentificationStatus { Unidentified, IdentifiedIncomplete, Identified }
