using HospitalManagement.Api.Data;
using HospitalManagement.Api.DTOs;
using HospitalManagement.Api.Exceptions;
using HospitalManagement.Api.Helpers;
using HospitalManagement.Api.Interfaces;
using HospitalManagement.Api.Models;
using HospitalManagement.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagement.Api.Services;

/// <summary>
/// Emergency registration: a normal <see cref="Patient"/> record (flagged IsEmergencyRegistration) plus an admission
/// created through the same tables the Admissions module uses. Everything a registration touches is saved in ONE
/// SaveChanges, so the patient, admission, bed and audit rows are all-or-nothing.
/// </summary>
public class EmergencyService(AppDbContext dbContext, ILogger<EmergencyService> logger) : IEmergencyService
{
    private const string EmergencyDepartmentCode = "EMR";
    private const string EmergencyDepartmentName = "Emergency";

    // ------------------------------------------------------------------ Lookup
    public async Task<EmergencyLookupDto> GetLookupAsync(CancellationToken cancellationToken)
    {
        var doctors = await dbContext.Doctors
            .AsNoTracking()
            .Include(item => item.User)
            .Where(item => item.Status == StaffStatus.Active && item.User.IsActive)
            .OrderBy(item => item.User.LastName).ThenBy(item => item.User.FirstName)
            .ToListAsync(cancellationToken);

        var beds = await dbContext.Beds
            .AsNoTracking()
            .Include(item => item.Room).ThenInclude(item => item.Ward)
            .Where(item => item.Status == BedStatus.Available && item.Room.IsActive && item.Room.Ward.IsActive)
            .ToListAsync(cancellationToken);

        return new EmergencyLookupDto(
            doctors.Select(item => new EmergencyDoctorOptionDto(
                item.Id, $"{item.User.FirstName} {item.User.LastName}", item.Specialization)).ToList(),
            beds
                .OrderByDescending(item => item.Room.Ward.Type == WardType.Emergency) // emergency wards first
                .ThenBy(item => item.Room.Ward.Name).ThenBy(item => item.BedNumber)
                .Select(item => new EmergencyBedOptionDto(
                    item.Id, BedLabel(item), item.Room.Ward.Name, item.Room.Ward.Type))
                .ToList());
    }

    // ------------------------------------------------------------------ Register
    public async Task<EmergencyRegistrationResultDto> RegisterAsync(
        RegisterEmergencyPatientRequest request,
        AuditActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.UserId is null)
            throw new UnauthorizedAppException();

        if (!Enum.IsDefined(request.EmergencyType))
            throw new RequestValidationException("Select a valid emergency type.");
        if (request.IdentificationStatus is not (IdentificationStatus.Unidentified or IdentificationStatus.IdentifiedIncomplete))
            throw new RequestValidationException("Identification status must be Unidentified or Identified but details incomplete.");
        if (request.Gender is { } requestedGender && !Enum.IsDefined(requestedGender))
            throw new RequestValidationException("Select a valid gender.");
        if (request.ApproximateAge is < 0 or > 130)
            throw new RequestValidationException("Approximate age must be between 0 and 130.");

        var (firstName, lastName, hasName) = ParseName(request.PatientName);
        if (request.IdentificationStatus == IdentificationStatus.IdentifiedIncomplete && !hasName)
            throw new RequestValidationException("Enter the patient's name when the patient is identified.");

        var notes = string.IsNullOrWhiteSpace(request.EmergencyNotes) ? null : request.EmergencyNotes.Trim();
        if (notes is { Length: > 1000 })
            throw new RequestValidationException("Emergency notes cannot be longer than 1000 characters.");

        Guid? doctorId = null;
        string? doctorName = null;
        if (request.DoctorId is Guid requestedDoctorId && requestedDoctorId != Guid.Empty)
        {
            var doctor = await dbContext.Doctors
                .AsNoTracking()
                .Include(item => item.User)
                .SingleOrDefaultAsync(item => item.Id == requestedDoctorId, cancellationToken)
                ?? throw new NotFoundException(nameof(Doctor), requestedDoctorId);
            if (doctor.Status != StaffStatus.Active || !doctor.User.IsActive)
                throw new RequestValidationException("The selected doctor is not active.");
            doctorId = doctor.Id;
            doctorName = $"{doctor.User.FirstName} {doctor.User.LastName}";
        }

        Guid? requestedBedId = request.BedId is Guid bedGuid && bedGuid != Guid.Empty ? bedGuid : null;
        var departmentId = await EnsureEmergencyDepartmentAsync(cancellationToken);

        // Numbers are drawn once, from database sequences: concurrent registrations can never receive the same value.
        var patientNumber = await PatientNumberGenerator.NextEmergencyAsync(dbContext, cancellationToken);
        var admissionNumber = await NextAdmissionNumberAsync(cancellationToken);
        var patientId = Guid.NewGuid();
        var admissionId = Guid.NewGuid();

        for (var attempt = 1; ; attempt++)
        {
            var warnings = new List<string>();
            var bedToUse = attempt == 1 ? requestedBedId : null;
            if (attempt > 1)
                warnings.Add("The selected bed was taken by someone else just now, so the patient was registered without a bed. Assign a bed from the patient dashboard.");

            Bed? bed = null;
            if (bedToUse is Guid chosenBedId)
            {
                bed = await dbContext.Beds
                    .Include(item => item.Room).ThenInclude(item => item.Ward)
                    .SingleOrDefaultAsync(item => item.Id == chosenBedId, cancellationToken)
                    ?? throw new NotFoundException(nameof(Bed), chosenBedId);
                if (bed.Status != BedStatus.Available)
                {
                    warnings.Add("The selected bed is no longer available, so the patient was registered without a bed. Assign a bed from the patient dashboard.");
                    bed = null;
                }
            }
            else if (requestedBedId is null)
            {
                warnings.Add(await dbContext.Beds.AnyAsync(item => item.Status == BedStatus.Available, cancellationToken)
                    ? "No bed was selected. Assign a bed from the patient dashboard."
                    : "No beds are currently available. The patient was registered without a bed - assign one as soon as a bed is free.");
            }

            if (doctorId is null)
                warnings.Add("No doctor is assigned yet. Assign a doctor from the patient dashboard.");

            var now = DateTime.UtcNow;
            var patient = new Patient
            {
                Id = patientId,
                PatientNumber = patientNumber,
                FirstName = firstName,
                LastName = lastName,
                Gender = request.Gender ?? Gender.Unknown,
                ApproximateAge = request.ApproximateAge,
                RegisteredAt = now,
                Status = PatientStatus.Active,
                IsEmergencyRegistration = true,
                EmergencyType = request.EmergencyType,
                IdentificationStatus = request.IdentificationStatus,
                EmergencyNotes = notes,
                RegisteredByUserId = actor.UserId,
            };
            var admission = new Admission
            {
                Id = admissionId,
                AdmissionNumber = admissionNumber,
                PatientId = patientId,
                DoctorId = doctorId,
                DepartmentId = departmentId,
                AdmittedAt = now,
                Reason = BuildReason(request.EmergencyType, notes),
                Status = AdmissionStatus.Admitted,
            };

            dbContext.Patients.Add(patient);
            dbContext.Admissions.Add(admission);
            AuditWriter.Stage(dbContext, actor, "EmergencyPatientRegistered", nameof(Patient), patientId.ToString(),
                $"Emergency patient {patientNumber} registered ({request.EmergencyType}, {request.IdentificationStatus}).");
            AuditWriter.Stage(dbContext, actor, "EmergencyAdmissionCreated", nameof(Admission), admissionId.ToString(),
                $"Emergency admission {admissionNumber} created for {patientNumber}.");
            if (doctorId is not null)
                AuditWriter.Stage(dbContext, actor, "EmergencyDoctorAssigned", nameof(Admission), admissionId.ToString(),
                    $"Doctor {doctorName} assigned to {admissionNumber}.");

            string? bedLabel = null;
            if (bed is not null)
            {
                bedLabel = BedLabel(bed);
                dbContext.BedAssignments.Add(new BedAssignment
                {
                    AdmissionId = admissionId,
                    BedId = bed.Id,
                    AssignedAt = now,
                    DailyRateSnapshot = bed.Room.DailyRate,
                });
                bed.Status = BedStatus.Occupied;
                AuditWriter.Stage(dbContext, actor, "EmergencyBedAssigned", nameof(Bed), bed.Id.ToString(),
                    $"Bed {bedLabel} assigned to {patientNumber} ({admissionNumber}).");
            }

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (attempt == 1 && bed is not null && DbErrors.IsUniqueViolation(ex))
            {
                // Another user took the same bed at the same moment (only one open assignment per bed is allowed).
                // Discard this attempt and register the patient without the bed instead of failing the emergency.
                dbContext.ChangeTracker.Clear();
                logger.LogWarning(ex, "Bed {BedId} was taken concurrently while registering {PatientNumber}", bed.Id, patientNumber);
                continue;
            }
            catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
            {
                dbContext.ChangeTracker.Clear();
                logger.LogError(ex, "Unique violation while registering emergency patient {PatientNumber}", patientNumber);
                throw new ConflictException("The emergency patient could not be registered because of a conflicting record. Please try again.");
            }

            logger.LogInformation("Emergency patient {PatientNumber} registered by {UserId}", patientNumber, actor.UserId);
            return new EmergencyRegistrationResultDto(
                patientId, patientNumber, $"{firstName} {lastName}", admissionId, admissionNumber,
                bed is not null, bedLabel, warnings);
        }
    }

    // ------------------------------------------------------------------ Dashboard
    public async Task<EmergencyDashboardDto> GetDashboardAsync(
        Guid patientId,
        EmergencyViewAccess access,
        CancellationToken cancellationToken)
    {
        var patient = await dbContext.Patients
            .AsNoTracking()
            .Include(item => item.RegisteredByUser)
            .SingleOrDefaultAsync(item => item.Id == patientId && item.IsEmergencyRegistration, cancellationToken)
            ?? throw new NotFoundException("Emergency patient was not found.");

        var admission = await dbContext.Admissions
            .AsNoTracking()
            .Include(item => item.Doctor).ThenInclude(item => item!.User)
            .Include(item => item.Discharge)
            .Include(item => item.BedAssignments).ThenInclude(item => item.Bed).ThenInclude(item => item.Room).ThenInclude(item => item.Ward)
            .Where(item => item.PatientId == patientId)
            .OrderByDescending(item => item.AdmittedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var timeline = new List<EmergencyTimelineItemDto>
        {
            new(patient.RegisteredAt, "Registration",
                $"Emergency registration ({patient.EmergencyType})",
                patient.RegisteredByUser is null ? null : $"By {patient.RegisteredByUser.FirstName} {patient.RegisteredByUser.LastName}"),
        };

        EmergencyAdmissionDto? admissionDto = null;
        if (admission is not null)
        {
            admissionDto = ToAdmissionDto(admission);
            timeline.Add(new(admission.AdmittedAt, "Admission", $"Admitted ({admission.AdmissionNumber})", admission.Reason));
            foreach (var assignment in admission.BedAssignments)
            {
                timeline.Add(new(assignment.AssignedAt, "Bed", $"Bed assigned: {BedLabel(assignment.Bed)}", null));
                if (assignment.ReleasedAt is { } releasedAt)
                    timeline.Add(new(releasedAt, "Bed", $"Bed released: {BedLabel(assignment.Bed)}", null));
            }
            if (admission.Discharge is not null)
                timeline.Add(new(admission.Discharge.DischargedAt, "Discharge", $"Discharged ({admission.Discharge.Type})", admission.Discharge.Summary));
        }

        var restricted = new List<string>();

        List<EmergencyPrescriptionDto>? prescriptions = null;
        if (access.Prescriptions)
        {
            var rows = await dbContext.Prescriptions
                .AsNoTracking()
                .Include(item => item.Doctor).ThenInclude(item => item.User)
                .Include(item => item.Items).ThenInclude(item => item.Medicine)
                .Where(item => item.PatientId == patientId)
                .OrderByDescending(item => item.PrescribedAt)
                .Take(50)
                .ToListAsync(cancellationToken);
            prescriptions = rows.Select(item => new EmergencyPrescriptionDto(
                item.Id, item.PrescriptionNumber, item.PrescribedAt, item.Status,
                $"{item.Doctor.User.FirstName} {item.Doctor.User.LastName}",
                item.Items.Select(line => $"{line.Medicine.Name} {line.Dosage}".Trim()).ToList())).ToList();
            timeline.AddRange(rows.Select(item => new EmergencyTimelineItemDto(
                item.PrescribedAt, "Prescription", $"Prescription {item.PrescriptionNumber}",
                string.Join(", ", item.Items.Select(line => line.Medicine.Name)))));
        }
        else restricted.Add("Prescriptions");

        List<EmergencyLabOrderDto>? labOrders = null;
        if (access.Laboratory)
        {
            var rows = await dbContext.LabOrders
                .AsNoTracking()
                .Include(item => item.LabTest)
                .Where(item => item.PatientId == patientId)
                .OrderByDescending(item => item.OrderedAt)
                .Take(50)
                .ToListAsync(cancellationToken);
            labOrders = rows.Select(item => new EmergencyLabOrderDto(
                item.Id, item.OrderNumber, item.OrderedAt, item.LabTest.Name, item.Priority, item.Status)).ToList();
            timeline.AddRange(rows.Select(item => new EmergencyTimelineItemDto(
                item.OrderedAt, "Laboratory", $"Lab order {item.OrderNumber}: {item.LabTest.Name}", item.Status.ToString())));
        }
        else restricted.Add("Laboratory orders");

        if (access.Nursing)
        {
            var vitals = await dbContext.VitalSigns
                .AsNoTracking()
                .Where(item => item.PatientId == patientId)
                .OrderByDescending(item => item.RecordedAt)
                .Take(50)
                .ToListAsync(cancellationToken);
            timeline.AddRange(vitals.Select(item => new EmergencyTimelineItemDto(
                item.RecordedAt, "Vital signs", "Vital signs recorded", VitalSummary(item))));

            var notesList = await dbContext.NursingNotes
                .AsNoTracking()
                .Where(item => item.PatientId == patientId)
                .OrderByDescending(item => item.CreatedAt)
                .Take(50)
                .ToListAsync(cancellationToken);
            timeline.AddRange(notesList.Select(item => new EmergencyTimelineItemDto(
                item.CreatedAt, "Nursing note", "Nursing note", item.Note)));
        }
        else restricted.Add("Nursing notes and vital signs");

        return new EmergencyDashboardDto(
            patient.Id,
            patient.PatientNumber,
            $"{patient.FirstName} {patient.LastName}",
            patient.ApproximateAge,
            patient.DateOfBirth,
            patient.Gender,
            patient.IdentificationStatus,
            patient.EmergencyType,
            patient.EmergencyNotes,
            patient.RegisteredAt,
            patient.RegisteredByUser is null ? null : $"{patient.RegisteredByUser.FirstName} {patient.RegisteredByUser.LastName}",
            patient.Phone,
            patient.Address,
            patient.EmergencyContactName,
            patient.EmergencyContactPhone,
            admissionDto,
            prescriptions,
            labOrders,
            timeline.OrderByDescending(item => item.At).ToList(),
            restricted);
    }

    // ------------------------------------------------------------------ Assign doctor / bed later
    public async Task<EmergencyAdmissionDto> AssignCareAsync(
        Guid patientId,
        AssignEmergencyCareRequest request,
        AuditActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);

        Guid? doctorId = request.DoctorId is Guid d && d != Guid.Empty ? d : null;
        Guid? bedId = request.BedId is Guid b && b != Guid.Empty ? b : null;
        if (doctorId is null && bedId is null)
            throw new RequestValidationException("Select a doctor, a bed, or both.");

        var patient = await dbContext.Patients
            .SingleOrDefaultAsync(item => item.Id == patientId && item.IsEmergencyRegistration, cancellationToken)
            ?? throw new NotFoundException("Emergency patient was not found.");

        Doctor? doctor = null;
        if (doctorId is Guid wantedDoctorId)
        {
            doctor = await dbContext.Doctors.Include(item => item.User)
                .SingleOrDefaultAsync(item => item.Id == wantedDoctorId, cancellationToken)
                ?? throw new NotFoundException(nameof(Doctor), wantedDoctorId);
            if (doctor.Status != StaffStatus.Active || !doctor.User.IsActive)
                throw new RequestValidationException("The selected doctor is not active.");
        }

        var admission = await dbContext.Admissions
            .Include(item => item.BedAssignments).ThenInclude(item => item.Bed).ThenInclude(item => item.Room).ThenInclude(item => item.Ward)
            .SingleOrDefaultAsync(item => item.PatientId == patientId &&
                (item.Status == AdmissionStatus.Admitted || item.Status == AdmissionStatus.UnderTreatment), cancellationToken);

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        if (admission is null)
        {
            // The patient has no active admission (for example after a discharge): open a new emergency admission.
            admission = new Admission
            {
                Id = Guid.NewGuid(),
                AdmissionNumber = await NextAdmissionNumberAsync(cancellationToken),
                PatientId = patientId,
                DepartmentId = await EnsureEmergencyDepartmentAsync(cancellationToken),
                AdmittedAt = DateTime.UtcNow,
                Reason = BuildReason(patient.EmergencyType ?? EmergencyType.Other, patient.EmergencyNotes),
                Status = AdmissionStatus.Admitted,
            };
            dbContext.Admissions.Add(admission);
            AuditWriter.Stage(dbContext, actor, "EmergencyAdmissionCreated", nameof(Admission), admission.Id.ToString(),
                $"Emergency admission {admission.AdmissionNumber} created for {patient.PatientNumber}.");
        }

        if (doctor is not null && admission.DoctorId != doctor.Id)
        {
            var previous = admission.DoctorId is null ? "none" : admission.DoctorId.ToString();
            admission.DoctorId = doctor.Id;
            AuditWriter.Stage(dbContext, actor, "EmergencyDoctorAssigned", nameof(Admission), admission.Id.ToString(),
                $"Doctor {doctor.User.FirstName} {doctor.User.LastName} assigned to {admission.AdmissionNumber} (previous doctor: {previous}).");
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (bedId is Guid wantedBedId)
        {
            var bed = await dbContext.Beds
                .Include(item => item.Room).ThenInclude(item => item.Ward)
                .SingleOrDefaultAsync(item => item.Id == wantedBedId, cancellationToken)
                ?? throw new NotFoundException(nameof(Bed), wantedBedId);

            var current = admission.BedAssignments.FirstOrDefault(item => item.ReleasedAt == null);
            if (current?.BedId == bed.Id)
                throw new ConflictException("The patient is already in this bed.");
            if (bed.Status != BedStatus.Available)
                throw new ConflictException("The selected bed is not available.");

            var now = DateTime.UtcNow;
            if (current is not null)
            {
                // Transfer: close the old assignment first (only one open assignment per admission is allowed).
                current.ReleasedAt = now;
                if (current.Bed.Status == BedStatus.Occupied)
                    current.Bed.Status = BedStatus.Available;
                AuditWriter.Stage(dbContext, actor, "EmergencyBedReleased", nameof(Bed), current.BedId.ToString(),
                    $"Bed {BedLabel(current.Bed)} released ({admission.AdmissionNumber}).");
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            dbContext.BedAssignments.Add(new BedAssignment
            {
                AdmissionId = admission.Id,
                BedId = bed.Id,
                AssignedAt = now,
                DailyRateSnapshot = bed.Room.DailyRate,
            });
            bed.Status = BedStatus.Occupied;
            AuditWriter.Stage(dbContext, actor, "EmergencyBedAssigned", nameof(Bed), bed.Id.ToString(),
                $"Bed {BedLabel(bed)} assigned to {patient.PatientNumber} ({admission.AdmissionNumber}).");

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
            {
                throw new ConflictException("That bed was just taken by someone else. Choose another bed.");
            }
        }

        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);

        dbContext.ChangeTracker.Clear();
        var reloaded = await dbContext.Admissions
            .AsNoTracking()
            .Include(item => item.Doctor).ThenInclude(item => item!.User)
            .Include(item => item.BedAssignments).ThenInclude(item => item.Bed).ThenInclude(item => item.Room).ThenInclude(item => item.Ward)
            .SingleAsync(item => item.Id == admission.Id, cancellationToken);
        return ToAdmissionDto(reloaded);
    }

    // ------------------------------------------------------------------ Complete the identity later
    public async Task<PatientListItemDto> CompleteDetailsAsync(
        Guid patientId,
        CompleteEmergencyPatientRequest request,
        AuditActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);

        var patient = await dbContext.Patients
            .SingleOrDefaultAsync(item => item.Id == patientId && item.IsEmergencyRegistration, cancellationToken)
            ?? throw new NotFoundException("Emergency patient was not found.");

        var firstName = request.FirstName?.Trim() ?? string.Empty;
        var lastName = request.LastName?.Trim() ?? string.Empty;
        if (firstName.Length == 0 || lastName.Length == 0)
            throw new RequestValidationException("First name and last name are required to identify the patient.");
        if (firstName.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
            lastName.Equals("Patient", StringComparison.OrdinalIgnoreCase))
            throw new RequestValidationException("Enter the patient's real name.");
        if (request.DateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
            throw new RequestValidationException("Date of birth cannot be in the future.");
        if (!Enum.IsDefined(request.Gender))
            throw new RequestValidationException("Select a valid gender.");

        var phone = request.Phone?.Trim() ?? string.Empty;
        if (phone.Length > 0 && !new System.ComponentModel.DataAnnotations.PhoneAttribute().IsValid(phone))
            throw new RequestValidationException("Phone number is invalid.");
        var contactPhone = request.EmergencyContactPhone?.Trim() ?? string.Empty;
        if (contactPhone.Length > 0 && !new System.ComponentModel.DataAnnotations.PhoneAttribute().IsValid(contactPhone))
            throw new RequestValidationException("Emergency contact phone number is invalid.");

        // Prevent a duplicate record: the same person may already be registered normally.
        var firstLower = firstName.ToLower();
        var lastLower = lastName.ToLower();
        var dateOfBirth = request.DateOfBirth;
        var duplicate = await dbContext.Patients
            .AsNoTracking()
            .Where(item => item.Id != patientId &&
                item.FirstName.ToLower() == firstLower &&
                item.LastName.ToLower() == lastLower &&
                ((dateOfBirth != null && item.DateOfBirth == dateOfBirth) || (phone != "" && item.Phone == phone)))
            .Select(item => item.PatientNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (duplicate is not null)
            throw new ConflictException(
                $"A patient with the same name and date of birth or phone number is already registered ({duplicate}). Open that record instead of creating a duplicate.");

        // Capture the old values for the audit trail before overwriting them.
        var changes = new List<string>();
        void Track(string label, string? before, string? after)
        {
            var oldValue = string.IsNullOrWhiteSpace(before) ? "(empty)" : before;
            var newValue = string.IsNullOrWhiteSpace(after) ? "(empty)" : after;
            if (oldValue != newValue)
                changes.Add($"{label}: '{oldValue}' -> '{newValue}'");
        }

        var address = request.Address?.Trim() ?? string.Empty;
        var city = request.City?.Trim() ?? string.Empty;
        var state = request.State?.Trim() ?? string.Empty;
        var contactName = request.EmergencyContactName?.Trim() ?? string.Empty;

        Track("Name", $"{patient.FirstName} {patient.LastName}", $"{firstName} {lastName}");
        Track("Date of birth", patient.DateOfBirth?.ToString("yyyy-MM-dd"), request.DateOfBirth?.ToString("yyyy-MM-dd"));
        Track("Gender", patient.Gender.ToString(), request.Gender.ToString());
        Track("Phone", patient.Phone, phone);
        Track("Address", patient.Address, address);
        Track("City", patient.City, city);
        Track("State", patient.State, state);
        Track("Emergency contact", patient.EmergencyContactName, contactName);
        Track("Emergency contact phone", patient.EmergencyContactPhone, contactPhone);
        Track("Identification", patient.IdentificationStatus.ToString(), IdentificationStatus.Identified.ToString());

        patient.FirstName = firstName;
        patient.LastName = lastName;
        patient.DateOfBirth = request.DateOfBirth;
        patient.Gender = request.Gender;
        patient.Phone = phone;
        patient.Address = address;
        patient.City = city;
        patient.State = state;
        patient.EmergencyContactName = contactName;
        patient.EmergencyContactPhone = contactPhone;
        patient.IdentificationStatus = IdentificationStatus.Identified;
        // PatientNumber, EmergencyType, RegisteredAt and every admission / prescription / lab / nursing row stay untouched.

        AuditWriter.Stage(dbContext, actor, "EmergencyIdentityCompleted", nameof(Patient), patient.Id.ToString(),
            $"Identity completed for {patient.PatientNumber}. " + (changes.Count == 0 ? "No value changed." : string.Join("; ", changes)));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            throw new ConflictException("The patient details could not be saved because they conflict with an existing record.");
        }

        return new PatientListItemDto(
            patient.Id, patient.PatientNumber, patient.FirstName, patient.LastName, patient.DateOfBirth, patient.Gender,
            patient.Phone, patient.Email, patient.City, patient.State, patient.Status, patient.RegisteredAt, patient.BloodGroup,
            patient.Address, patient.PostalCode, patient.EmergencyContactName, patient.EmergencyContactPhone,
            patient.InsuranceProvider, patient.InsuranceNumber, patient.UserId != null,
            patient.IsEmergencyRegistration, patient.IdentificationStatus);
    }

    // ------------------------------------------------------------------ helpers
    private static (string First, string Last, bool HasName) ParseName(string? name)
    {
        var parts = (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return ("Unknown", "Patient", false);

        var first = parts[0];
        var last = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : "Unknown";
        if (first.Length > 100 || last.Length > 100)
            throw new RequestValidationException("Each part of the name must be 100 characters or fewer.");
        return (first, last, true);
    }

    private static string BuildReason(EmergencyType type, string? notes)
    {
        var reason = $"Emergency: {type}" + (string.IsNullOrWhiteSpace(notes) ? string.Empty : $" - {notes.Trim()}");
        return reason.Length <= 1000 ? reason : reason[..1000];
    }

    private static string BedLabel(Bed bed) => $"{bed.Room.Ward.Name} / Room {bed.Room.RoomNumber} / {bed.BedNumber}";

    private static EmergencyAdmissionDto ToAdmissionDto(Admission admission)
    {
        var current = admission.BedAssignments.FirstOrDefault(item => item.ReleasedAt == null);
        return new EmergencyAdmissionDto(
            admission.Id,
            admission.AdmissionNumber,
            admission.Status,
            admission.AdmittedAt,
            admission.Reason,
            admission.DoctorId,
            admission.Doctor is null ? null : $"{admission.Doctor.User.FirstName} {admission.Doctor.User.LastName}",
            current?.Bed.Room.Ward.Name,
            current?.Bed.Room.RoomNumber,
            current?.Bed.BedNumber);
    }

    private static string VitalSummary(VitalSign vital)
    {
        var parts = new List<string>();
        if (vital.SystolicBp is not null && vital.DiastolicBp is not null) parts.Add($"BP {vital.SystolicBp}/{vital.DiastolicBp}");
        if (vital.HeartRate is not null) parts.Add($"HR {vital.HeartRate}");
        if (vital.RespiratoryRate is not null) parts.Add($"RR {vital.RespiratoryRate}");
        if (vital.OxygenSaturation is not null) parts.Add($"SpO2 {vital.OxygenSaturation}%");
        if (vital.TemperatureCelsius is not null) parts.Add($"Temp {vital.TemperatureCelsius}C");
        return parts.Count == 0 ? "No values" : string.Join(", ", parts);
    }

    /// <summary>The department every emergency admission belongs to. Reuses an existing "Emergency" department if one exists.</summary>
    private async Task<Guid> EnsureEmergencyDepartmentAsync(CancellationToken cancellationToken)
    {
        var existing = await FindEmergencyDepartmentAsync(cancellationToken);
        if (existing is not null)
            return existing.Value;

        var department = new Department
        {
            Id = Guid.NewGuid(),
            Name = EmergencyDepartmentName,
            Code = EmergencyDepartmentCode,
            Description = "Emergency and trauma care",
            IsActive = true,
        };
        dbContext.Departments.Add(department);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return department.Id;
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // Another request created it first.
            dbContext.Entry(department).State = EntityState.Detached;
            return await FindEmergencyDepartmentAsync(cancellationToken)
                ?? throw new ConflictException("The Emergency department could not be created.");
        }
    }

    private async Task<Guid?> FindEmergencyDepartmentAsync(CancellationToken cancellationToken)
    {
        var id = await dbContext.Departments
            .AsNoTracking()
            .Where(item => item.Code == EmergencyDepartmentCode || item.Name.ToLower() == EmergencyDepartmentName.ToLower())
            .Select(item => (Guid?)item.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return id;
    }

    private async Task<string> NextAdmissionNumberAsync(CancellationToken cancellationToken)
    {
        long sequenceNumber;
        if (dbContext.Database.IsRelational())
        {
            sequenceNumber = await dbContext.Database
                .SqlQueryRaw<long>($"SELECT nextval('{DbSequences.AdmissionNumber}') AS \"Value\"")
                .SingleAsync(cancellationToken);
        }
        else
        {
            sequenceNumber = await dbContext.Admissions.LongCountAsync(cancellationToken) + 1;
        }

        return $"ADM-{DateTime.UtcNow.Year}-{sequenceNumber:D6}";
    }
}
