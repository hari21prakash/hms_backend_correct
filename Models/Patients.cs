using HospitalManagement.Api.Models.Enums;

namespace HospitalManagement.Api.Models;

public class Patient : BaseEntity
{
    /// <summary>Human-readable unique number generated from a database sequence.</summary>
    public string PatientNumber { get; set; } = string.Empty;

    /// <summary>Optional login account for the patient portal.</summary>
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    /// <summary>Null until known (self-registered patients complete their profile later).</summary>
    public DateOnly? DateOfBirth { get; set; }
    public Gender Gender { get; set; }
    public BloodGroup BloodGroup { get; set; } = BloodGroup.Unknown;

    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? PostalCode { get; set; }

    public string EmergencyContactName { get; set; } = string.Empty;
    public string EmergencyContactPhone { get; set; } = string.Empty;

    public string? InsuranceProvider { get; set; }
    public string? InsuranceNumber { get; set; }

    public DateTime RegisteredAt { get; set; }
    public string? ProfilePhotoPath { get; set; }
    public PatientStatus Status { get; set; } = PatientStatus.Active;

    // ----- Emergency registration (all stay at their defaults for normal registrations) -----
    /// <summary>True when the record was created through Emergency Registration. Stays true after the identity is completed.</summary>
    public bool IsEmergencyRegistration { get; set; }
    public EmergencyType? EmergencyType { get; set; }
    public IdentificationStatus IdentificationStatus { get; set; } = IdentificationStatus.Identified;
    public int? ApproximateAge { get; set; }
    public string? EmergencyNotes { get; set; }
    /// <summary>Staff member who registered the patient (set for emergency registrations).</summary>
    public Guid? RegisteredByUserId { get; set; }
    public User? RegisteredByUser { get; set; }

    public ICollection<PatientAllergy> Allergies { get; set; } = new List<PatientAllergy>();
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
    public ICollection<MedicalRecord> MedicalRecords { get; set; } = new List<MedicalRecord>();
    public ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();
    public ICollection<LabOrder> LabOrders { get; set; } = new List<LabOrder>();
    public ICollection<Admission> Admissions { get; set; } = new List<Admission>();
    public ICollection<VitalSign> VitalSigns { get; set; } = new List<VitalSign>();
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
}

public class PatientAllergy : BaseEntity
{
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;

    public string Allergen { get; set; } = string.Empty;
    public string? Reaction { get; set; }
    public AllergySeverity Severity { get; set; } = AllergySeverity.Mild;
}
