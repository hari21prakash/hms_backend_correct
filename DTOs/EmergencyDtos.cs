using System.ComponentModel.DataAnnotations;
using HospitalManagement.Api.Models.Enums;

namespace HospitalManagement.Api.DTOs;

public class RegisterEmergencyPatientRequest
{
    /// <summary>Optional. Blank means "Unknown Patient".</summary>
    [StringLength(200)]
    public string? PatientName { get; set; }

    [Range(0, 130)]
    public int? ApproximateAge { get; set; }

    /// <summary>Optional. Null means Unknown.</summary>
    public Gender? Gender { get; set; }

    public EmergencyType EmergencyType { get; set; } = EmergencyType.Other;

    /// <summary>Unidentified or IdentifiedIncomplete. (Identified is reached by completing the details later.)</summary>
    public IdentificationStatus IdentificationStatus { get; set; } = IdentificationStatus.Unidentified;

    [StringLength(1000)]
    public string? EmergencyNotes { get; set; }

    /// <summary>Optional. When omitted the admission waits for a doctor to be assigned.</summary>
    public Guid? DoctorId { get; set; }

    /// <summary>Optional. When omitted (or no longer available) the patient is registered without a bed.</summary>
    public Guid? BedId { get; set; }
}

public record EmergencyRegistrationResultDto(
    Guid PatientId,
    string PatientNumber,
    string PatientName,
    Guid AdmissionId,
    string AdmissionNumber,
    bool BedAssigned,
    string? BedLabel,
    IReadOnlyList<string> Warnings);

public record EmergencyDoctorOptionDto(Guid Id, string Name, string Specialization);

public record EmergencyBedOptionDto(Guid Id, string Label, string WardName, WardType WardType);

public record EmergencyLookupDto(
    IReadOnlyList<EmergencyDoctorOptionDto> Doctors,
    IReadOnlyList<EmergencyBedOptionDto> Beds);

public class AssignEmergencyCareRequest
{
    public Guid? DoctorId { get; set; }
    public Guid? BedId { get; set; }
}

public class CompleteEmergencyPatientRequest
{
    [Required, StringLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string LastName { get; set; } = string.Empty;

    public DateOnly? DateOfBirth { get; set; }

    public Gender Gender { get; set; } = Gender.Unknown;

    [StringLength(30)]
    public string? Phone { get; set; }

    [StringLength(300)]
    public string? Address { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    [StringLength(100)]
    public string? State { get; set; }

    [StringLength(150)]
    public string? EmergencyContactName { get; set; }

    [StringLength(30)]
    public string? EmergencyContactPhone { get; set; }
}

public record EmergencyAdmissionDto(
    Guid Id,
    string AdmissionNumber,
    AdmissionStatus Status,
    DateTime AdmittedAt,
    string Reason,
    Guid? DoctorId,
    string? DoctorName,
    string? WardName,
    string? RoomNumber,
    string? BedNumber);

public record EmergencyPrescriptionDto(
    Guid Id,
    string PrescriptionNumber,
    DateTime PrescribedAt,
    PrescriptionStatus Status,
    string DoctorName,
    IReadOnlyList<string> Medicines);

public record EmergencyLabOrderDto(
    Guid Id,
    string OrderNumber,
    DateTime OrderedAt,
    string TestName,
    LabPriority Priority,
    LabOrderStatus Status);

public record EmergencyTimelineItemDto(DateTime At, string Type, string Title, string? Detail);

/// <summary>Which clinical sections the caller may see. Everything else comes back null and is listed in RestrictedSections.</summary>
public record EmergencyViewAccess(bool Prescriptions, bool Laboratory, bool Nursing);

public record EmergencyDashboardDto(
    Guid PatientId,
    string PatientNumber,
    string PatientName,
    int? ApproximateAge,
    DateOnly? DateOfBirth,
    Gender Gender,
    IdentificationStatus IdentificationStatus,
    EmergencyType? EmergencyType,
    string? EmergencyNotes,
    DateTime RegisteredAt,
    string? RegisteredBy,
    string Phone,
    string Address,
    string EmergencyContactName,
    string EmergencyContactPhone,
    EmergencyAdmissionDto? Admission,
    IReadOnlyList<EmergencyPrescriptionDto>? Prescriptions,
    IReadOnlyList<EmergencyLabOrderDto>? LabOrders,
    IReadOnlyList<EmergencyTimelineItemDto>? Timeline,
    IReadOnlyList<string> RestrictedSections);
