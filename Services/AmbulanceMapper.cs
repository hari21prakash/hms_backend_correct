using System.ComponentModel.DataAnnotations;
using System.Text;
using HospitalManagement.Api.Data;
using HospitalManagement.Api.DTOs;
using HospitalManagement.Api.Exceptions;
using HospitalManagement.Api.Models;
using HospitalManagement.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagement.Api.Services;

/// <summary>Entity -> DTO mapping shared by the ambulance services. Redaction (location, driver identity) happens here.</summary>
public static class AmbulanceMapper
{
    public static string FullName(User user) => $"{user.FirstName} {user.LastName}";

    public static AmbulanceLocationDto? ToLocation(Ambulance ambulance) =>
        ambulance.CurrentLatitude is { } lat && ambulance.CurrentLongitude is { } lon && ambulance.LastLocationAt is { } at
            ? new AmbulanceLocationDto(lat, lon, ambulance.LocationAccuracyMeters, ambulance.LocationSpeedKmh, at)
            : null;

    public static (bool Eligible, string? Reason) Eligibility(Ambulance ambulance, bool hasActiveAssignment)
    {
        if (!ambulance.IsActive) return (false, "The vehicle is inactive.");
        if (hasActiveAssignment) return (false, "The ambulance is already on an assignment.");
        if (ambulance.Status != AmbulanceStatus.Available) return (false, $"The ambulance is {ambulance.Status}.");
        if (ambulance.DriverUserId is null || ambulance.DriverUser is null) return (false, "No driver is assigned.");
        if (!ambulance.DriverUser.IsActive) return (false, "The driver's account is inactive.");
        if (ambulance.OwnerType == AmbulanceOwnerType.External && ambulance.Provider?.Status != ProviderStatus.Approved)
            return (false, $"The provider is {ambulance.Provider?.Status.ToString() ?? "unknown"}, not Approved.");
        return (true, null);
    }

    public static AmbulanceDto ToDto(
        Ambulance ambulance,
        AmbulanceViewOptions view,
        double? distanceKm,
        AmbulanceAssignment? activeAssignment)
    {
        var (eligible, reason) = Eligibility(ambulance, activeAssignment is not null);
        var driver = view.IncludeDriver ? ambulance.DriverUser : null;
        return new AmbulanceDto(
            ambulance.Id, ambulance.VehicleNumber, ambulance.Type, ambulance.OwnerType,
            ambulance.ProviderId, ambulance.Provider?.Name, ambulance.Provider?.Status,
            ambulance.Make, ambulance.Model, ambulance.Year, ambulance.Capacity, ambulance.Notes,
            ambulance.Status, ambulance.IsActive,
            ambulance.DriverUserId,
            driver is null ? null : FullName(driver),
            driver?.PhoneNumber,
            view.IncludeLocation ? ToLocation(ambulance) : null,
            eligible, reason, distanceKm,
            activeAssignment?.RequestId,
            activeAssignment?.Request?.RequestNumber,
            activeAssignment?.Status);
    }

    /// <summary>The assignment must have Request, Ambulance(+Provider) and DriverUser loaded.</summary>
    public static DispatchAssignmentDto ToAssignmentDto(AmbulanceAssignment assignment, AmbulanceViewOptions view)
    {
        var request = assignment.Request;
        var ambulance = assignment.Ambulance;
        var location = view.IncludeLocation ? ToLocation(ambulance) : null;
        double? distance = location is null || !IsActiveTrip(assignment) ? null :
            Math.Round(AmbulanceRules.DistanceKm(location.Latitude, location.Longitude, request.Latitude, request.Longitude), 2);
        return new DispatchAssignmentDto(
            assignment.Id, request.Id, request.RequestNumber, ambulance.Id, ambulance.VehicleNumber, ambulance.Type,
            ambulance.OwnerType, ambulance.Provider?.Name, assignment.Status, assignment.IsActive,
            view.IncludeDriver ? FullName(assignment.DriverUser) : null,
            view.IncludeDriver ? assignment.DriverUser.PhoneNumber : null,
            assignment.AssignedAt, assignment.AcceptedAt, assignment.StartedAt, assignment.ArrivedAt, assignment.PickedUpAt,
            assignment.CompletedAt, assignment.DeclineReason, location, distance);
    }

    private static bool IsActiveTrip(AmbulanceAssignment assignment) => assignment.IsActive && AmbulanceRules.IsTripActive(assignment.Status);

    public static DispatchRequestDto ToRequestDto(
        AmbulanceRequest request,
        AmbulanceAssignment? activeAssignment,
        IReadOnlyList<StatusHistoryItemDto>? history,
        AmbulanceViewOptions view)
    {
        return new DispatchRequestDto(
            request.Id, request.RequestNumber, request.Status, request.StatusReason,
            request.Latitude, request.Longitude, request.Address, request.EmergencyType, request.InjuredCount,
            request.RequesterName, request.ContactPhone, request.Description, request.CreatedAt,
            request.PatientId, request.Patient?.PatientNumber,
            request.Patient is null ? null : $"{request.Patient.FirstName} {request.Patient.LastName}",
            activeAssignment is null ? null : ToAssignmentDto(activeAssignment, view),
            history);
    }

    public static string StatusLabel(AmbulanceRequestStatus status) => status switch
    {
        AmbulanceRequestStatus.Requested => "Request received - waiting for the hospital",
        AmbulanceRequestStatus.Accepted => "Request accepted by the hospital",
        AmbulanceRequestStatus.Assigned => "Ambulance assigned",
        AmbulanceRequestStatus.Dispatched => "Ambulance is on the way",
        AmbulanceRequestStatus.Arrived => "Ambulance has arrived",
        AmbulanceRequestStatus.PatientPickedUp => "Patient picked up - heading to the hospital",
        AmbulanceRequestStatus.AtHospital => "Arrived at the hospital",
        AmbulanceRequestStatus.Completed => "Completed",
        AmbulanceRequestStatus.Cancelled => "Cancelled",
        AmbulanceRequestStatus.Rejected => "The request could not be accepted",
        _ => status.ToString(),
    };

    public static string AssignmentLabel(AssignmentStatus status) => status switch
    {
        AssignmentStatus.Pending => "Waiting for the driver to accept",
        AssignmentStatus.Accepted => "Driver accepted",
        AssignmentStatus.EnRoute => "On the way to you",
        AssignmentStatus.Arrived => "Arrived at the scene",
        AssignmentStatus.PickedUp => "Taking the patient to hospital",
        AssignmentStatus.Completed => "Trip completed",
        _ => status.ToString(),
    };
}

/// <summary>Creates login accounts for ambulance drivers / provider administrators. Stages entities; the caller saves.</summary>
public static class AmbulanceAccountFactory
{
    public static async Task<User> StageAsync(
        AppDbContext dbContext,
        string firstName,
        string lastName,
        string email,
        string? phone,
        string password,
        string roleName,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (!new EmailAddressAttribute().IsValid(normalizedEmail))
            throw new RequestValidationException("A valid email address is required.");
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            throw new RequestValidationException("First and last name are required.");
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            throw new RequestValidationException("Password must be at least 8 characters.");
        if (Encoding.UTF8.GetByteCount(password) > 72)
            throw new RequestValidationException("Password must be no more than 72 UTF-8 bytes.");
        if (!string.IsNullOrWhiteSpace(phone) && !new PhoneAttribute().IsValid(phone))
            throw new RequestValidationException("Phone number is invalid.");
        if (await dbContext.Users.AnyAsync(user => user.Email == normalizedEmail, cancellationToken))
            throw new ConflictException("An account with this email already exists.");

        var role = await dbContext.Roles.SingleOrDefaultAsync(item => item.Name == roleName, cancellationToken)
            ?? throw new RequestValidationException($"The role '{roleName}' is not available. Restart the API so the roles are seeded.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            RoleId = role.Id,
            Role = role,
            IsActive = true,
        };
        dbContext.Users.Add(user);
        return user;
    }

    /// <summary>True when the user's role carries the driver permission (permission-based, not a role-name check).</summary>
    public static Task<bool> IsDriverAsync(AppDbContext dbContext, Guid userId, CancellationToken cancellationToken) =>
        dbContext.Users.AnyAsync(user => user.Id == userId &&
            dbContext.RolePermissions.Any(link => link.RoleId == user.RoleId && link.Permission.Code == "ambulances.driver.status"),
            cancellationToken);
}
