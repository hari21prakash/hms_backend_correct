using System.Text.RegularExpressions;
using HospitalManagement.Api.Data;
using HospitalManagement.Api.DTOs;
using HospitalManagement.Api.Exceptions;
using HospitalManagement.Api.Helpers;
using HospitalManagement.Api.Interfaces;
using HospitalManagement.Api.Models;
using HospitalManagement.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagement.Api.Services;

/// <summary>Ambulance fleet management. Provider-linked callers are confined to their own provider's vehicles.</summary>
public class AmbulanceService(AppDbContext dbContext, IAmbulanceBroadcaster broadcaster) : IAmbulanceService
{
    private static readonly Regex VehicleNumberPattern = new(@"^[A-Z0-9][A-Z0-9 \-]{2,29}$", RegexOptions.Compiled);

    // ------------------------------------------------------------------ read
    public async Task<IReadOnlyList<AmbulanceDto>> ListAsync(
        AmbulanceQuery query, AmbulanceCaller caller, AmbulanceViewOptions view, CancellationToken cancellationToken)
    {
        var source = Scoped(dbContext.Ambulances.AsNoTracking().Include(a => a.Provider).Include(a => a.DriverUser), caller);
        if (query.Status is { } status) source = source.Where(a => a.Status == status);
        if (query.Type is { } type) source = source.Where(a => a.Type == type);
        if (query.OwnerType is { } owner) source = source.Where(a => a.OwnerType == owner);
        if (query.ProviderId is { } providerId) source = source.Where(a => a.ProviderId == providerId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToUpperInvariant();
            source = source.Where(a => a.VehicleNumber.Contains(term));
        }

        var items = await source.OrderBy(a => a.VehicleNumber).ToListAsync(cancellationToken);
        var active = await ActiveAssignmentsAsync(items.Select(a => a.Id), cancellationToken);
        return items.Select(a => AmbulanceMapper.ToDto(a, view, null, active.GetValueOrDefault(a.Id))).ToList();
    }

    public async Task<AmbulanceDto> GetAsync(Guid id, AmbulanceCaller caller, AmbulanceViewOptions view, CancellationToken cancellationToken)
    {
        var ambulance = await LoadAsync(id, tracking: false, cancellationToken);
        EnsureOwnership(ambulance, caller);
        return await ToDtoAsync(ambulance, view, cancellationToken);
    }

    public async Task<IReadOnlyList<AmbulanceDto>> GetAvailableAsync(
        Guid? requestId, AmbulanceCaller caller, AmbulanceViewOptions view, CancellationToken cancellationToken)
    {
        (double Lat, double Lon)? origin = null;
        if (requestId is { } id)
        {
            var request = await dbContext.AmbulanceRequests.AsNoTracking()
                .Where(item => item.Id == id).Select(item => new { item.Latitude, item.Longitude })
                .SingleOrDefaultAsync(cancellationToken) ?? throw new NotFoundException(nameof(AmbulanceRequest), id);
            origin = (request.Latitude, request.Longitude);
        }

        var candidates = await Scoped(dbContext.Ambulances.AsNoTracking().Include(a => a.Provider).Include(a => a.DriverUser), caller)
            .Where(a => a.IsActive && a.Status == AmbulanceStatus.Available && a.DriverUserId != null &&
                        (a.OwnerType == AmbulanceOwnerType.Hospital || a.Provider!.Status == ProviderStatus.Approved) &&
                        !dbContext.AmbulanceAssignments.Any(x => x.AmbulanceId == a.Id && x.IsActive))
            .ToListAsync(cancellationToken);

        return candidates
            .Select(a =>
            {
                var location = AmbulanceMapper.ToLocation(a);
                double? distance = view.IncludeLocation && origin is { } o && location is not null
                    ? Math.Round(AmbulanceRules.DistanceKm(location.Latitude, location.Longitude, o.Lat, o.Lon), 2)
                    : null;
                return AmbulanceMapper.ToDto(a, view, distance, null);
            })
            .OrderBy(a => a.DistanceKm ?? double.MaxValue).ThenBy(a => a.VehicleNumber)
            .ToList();
    }

    // ------------------------------------------------------------------ write
    public async Task<AmbulanceDto> CreateAsync(
        SaveAmbulanceRequest request, AmbulanceCaller caller, AuditActor actor, AmbulanceViewOptions view, CancellationToken cancellationToken)
    {
        var number = NormalizeVehicleNumber(request.VehicleNumber);

        Guid? providerId;
        if (caller.IsProviderScoped)
        {
            if (request.ProviderId is { } requested && requested != caller.ProviderId)
                throw new ForbiddenException("You can only register vehicles for your own provider.");
            providerId = caller.ProviderId;
            var provider = await dbContext.AmbulanceProviders.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == providerId, cancellationToken)
                ?? throw new NotFoundException(nameof(AmbulanceProvider), providerId!);
            if (provider.Status is ProviderStatus.Rejected or ProviderStatus.Suspended)
                throw new ForbiddenException($"Your provider is {provider.Status}; vehicles cannot be registered.");
        }
        else
        {
            providerId = request.ProviderId;
            if (providerId is { } id && !await dbContext.AmbulanceProviders.AnyAsync(item => item.Id == id, cancellationToken))
                throw new NotFoundException(nameof(AmbulanceProvider), id);
        }

        if (await dbContext.Ambulances.AnyAsync(a => a.VehicleNumber == number, cancellationToken))
            throw new ConflictException("An ambulance with this vehicle number already exists.");

        var ambulance = new Ambulance
        {
            Id = Guid.NewGuid(),
            VehicleNumber = number,
            Type = Defined(request.Type),
            OwnerType = providerId is null ? AmbulanceOwnerType.Hospital : AmbulanceOwnerType.External,
            ProviderId = providerId,
            Make = AmbulanceRules.Trim(request.Make, 100),
            Model = AmbulanceRules.Trim(request.Model, 100),
            Year = request.Year,
            Capacity = request.Capacity,
            Notes = AmbulanceRules.Trim(request.Notes, 500),
            IsActive = request.IsActive,
            Status = AmbulanceStatus.Available,
        };
        dbContext.Ambulances.Add(ambulance);
        dbContext.AmbulanceStatusHistories.Add(new AmbulanceStatusHistory
        {
            Id = Guid.NewGuid(), Scope = StatusHistoryScope.Ambulance, AmbulanceId = ambulance.Id,
            ToStatus = AmbulanceStatus.Available.ToString(), ChangedByUserId = caller.UserId, ChangedAt = DateTime.UtcNow, Note = "Registered",
        });
        AuditWriter.Stage(dbContext, actor, "AmbulanceRegistered", nameof(Ambulance), ambulance.Id.ToString(),
            $"Ambulance {number} registered ({ambulance.OwnerType}).");

        await SaveAsync(cancellationToken, "An ambulance with this vehicle number already exists.");
        var created = await LoadAsync(ambulance.Id, tracking: false, cancellationToken);
        return await ToDtoAsync(created, view, cancellationToken);
    }

    public async Task<AmbulanceDto> UpdateAsync(
        Guid id, SaveAmbulanceRequest request, AmbulanceCaller caller, AuditActor actor, AmbulanceViewOptions view, CancellationToken cancellationToken)
    {
        var ambulance = await LoadAsync(id, tracking: true, cancellationToken);
        EnsureOwnership(ambulance, caller);

        var number = NormalizeVehicleNumber(request.VehicleNumber);
        if (await dbContext.Ambulances.AnyAsync(a => a.Id != id && a.VehicleNumber == number, cancellationToken))
            throw new ConflictException("An ambulance with this vehicle number already exists.");
        if (!request.IsActive && await HasActiveAssignmentAsync(id, cancellationToken))
            throw new ConflictException("The ambulance is on an active assignment and cannot be deactivated now.");

        // Owner / provider / history are never changed here: only the vehicle details.
        ambulance.VehicleNumber = number;
        ambulance.Type = Defined(request.Type);
        ambulance.Make = AmbulanceRules.Trim(request.Make, 100);
        ambulance.Model = AmbulanceRules.Trim(request.Model, 100);
        ambulance.Year = request.Year;
        ambulance.Capacity = request.Capacity;
        ambulance.Notes = AmbulanceRules.Trim(request.Notes, 500);
        ambulance.IsActive = request.IsActive;
        AuditWriter.Stage(dbContext, actor, "AmbulanceUpdated", nameof(Ambulance), id.ToString(), $"Ambulance {number} details updated.");

        await SaveAsync(cancellationToken, "An ambulance with this vehicle number already exists.");
        await Publish(ambulance);
        return await ToDtoAsync(ambulance, view, cancellationToken);
    }

    public async Task<AmbulanceDto> SetAvailabilityAsync(
        Guid id, SetAvailabilityRequest request, AmbulanceCaller caller, AuditActor actor, AmbulanceViewOptions view, CancellationToken cancellationToken)
    {
        var ambulance = await LoadAsync(id, tracking: true, cancellationToken);
        EnsureOwnership(ambulance, caller);

        // Reserved / Dispatched / OnTrip belong to the dispatch workflow; staff may only switch between these two.
        if (request.Status is not (AmbulanceStatus.Available or AmbulanceStatus.OutOfService))
            throw new RequestValidationException("Only Available and OutOfService can be set manually. Reserved, Dispatched and OnTrip are set by dispatch.");
        if (await HasActiveAssignmentAsync(id, cancellationToken))
            throw new ConflictException("The ambulance is on an active assignment. Its status changes with the trip.");

        AmbulanceRules.MoveAmbulance(dbContext, ambulance, request.Status, caller.UserId, request.Note);
        AuditWriter.Stage(dbContext, actor, "AmbulanceAvailabilityChanged", nameof(Ambulance), id.ToString(),
            $"Ambulance {ambulance.VehicleNumber} set to {request.Status}.");
        await dbContext.SaveChangesAsync(cancellationToken);

        await Publish(ambulance);
        return await ToDtoAsync(ambulance, view, cancellationToken);
    }

    public async Task<AmbulanceDto> SetDriverAsync(
        Guid id, SetAmbulanceDriverRequest request, AmbulanceCaller caller, AuditActor actor, AmbulanceViewOptions view, CancellationToken cancellationToken)
    {
        var ambulance = await LoadAsync(id, tracking: true, cancellationToken);
        EnsureOwnership(ambulance, caller);
        if (await HasActiveAssignmentAsync(id, cancellationToken))
            throw new ConflictException("The driver cannot be changed while the ambulance is on an active assignment.");

        if (request.DriverUserId is not { } driverId || driverId == Guid.Empty)
        {
            ambulance.DriverUserId = null;
            ambulance.DriverUser = null;
        }
        else
        {
            var driver = await dbContext.Users.SingleOrDefaultAsync(user => user.Id == driverId, cancellationToken)
                ?? throw new NotFoundException("Driver", driverId);
            if (!driver.IsActive) throw new RequestValidationException("The selected driver's account is inactive.");
            if (!await AmbulanceAccountFactory.IsDriverAsync(dbContext, driverId, cancellationToken))
                throw new RequestValidationException("The selected user is not an ambulance driver.");

            var link = await dbContext.AmbulanceProviderUsers.AsNoTracking()
                .SingleOrDefaultAsync(item => item.UserId == driverId && item.IsActive, cancellationToken);
            if (ambulance.OwnerType == AmbulanceOwnerType.External)
            {
                if (link is null || link.ProviderId != ambulance.ProviderId || link.Role != ProviderUserRole.Driver)
                    throw new RequestValidationException("An external ambulance can only be driven by a driver of the same provider.");
            }
            else if (link is not null)
                throw new RequestValidationException("A provider's driver cannot drive a hospital-owned ambulance.");

            if (await dbContext.Ambulances.AnyAsync(a => a.Id != id && a.DriverUserId == driverId, cancellationToken))
                throw new ConflictException("This driver is already assigned to another ambulance.");

            ambulance.DriverUserId = driverId;
            ambulance.DriverUser = driver;
        }

        AuditWriter.Stage(dbContext, actor, "AmbulanceDriverAssigned", nameof(Ambulance), id.ToString(),
            $"Driver of {ambulance.VehicleNumber} set to {(ambulance.DriverUserId is null ? "none" : ambulance.DriverUserId.ToString())}.");
        await SaveAsync(cancellationToken, "This driver is already assigned to another ambulance.");
        await Publish(ambulance);
        return await ToDtoAsync(ambulance, view, cancellationToken);
    }

    // ------------------------------------------------------------------ drivers
    public async Task<IReadOnlyList<DriverOptionDto>> ListDriversAsync(AmbulanceCaller caller, CancellationToken cancellationToken)
    {
        List<User> users;
        Dictionary<Guid, AmbulanceProviderUser> links;
        if (caller.IsProviderScoped)
        {
            var providerLinks = await dbContext.AmbulanceProviderUsers.AsNoTracking().Include(item => item.User).Include(item => item.Provider)
                .Where(item => item.ProviderId == caller.ProviderId && item.Role == ProviderUserRole.Driver && item.IsActive)
                .ToListAsync(cancellationToken);
            users = providerLinks.Select(item => item.User).ToList();
            links = providerLinks.ToDictionary(item => item.UserId);
        }
        else
        {
            var providerUserIds = dbContext.AmbulanceProviderUsers.Select(item => item.UserId);
            users = await dbContext.Users.AsNoTracking()
                .Where(user => user.IsActive && !providerUserIds.Contains(user.Id) &&
                    dbContext.RolePermissions.Any(link => link.RoleId == user.RoleId && link.Permission.Code == "ambulances.driver.status"))
                .OrderBy(user => user.LastName).ThenBy(user => user.FirstName)
                .ToListAsync(cancellationToken);
            links = [];
        }

        var ids = users.Select(user => user.Id).ToList();
        var assigned = await dbContext.Ambulances.AsNoTracking()
            .Where(a => a.DriverUserId != null && ids.Contains(a.DriverUserId.Value))
            .ToDictionaryAsync(a => a.DriverUserId!.Value, cancellationToken);

        return users.Select(user =>
        {
            links.TryGetValue(user.Id, out var link);
            assigned.TryGetValue(user.Id, out var ambulance);
            return new DriverOptionDto(user.Id, AmbulanceMapper.FullName(user), user.Email, user.PhoneNumber,
                link?.ProviderId, link?.Provider?.Name, ambulance?.Id, ambulance?.VehicleNumber);
        }).ToList();
    }

    public async Task<DriverOptionDto> CreateHospitalDriverAsync(
        CreateAmbulanceDriverRequest request, AmbulanceCaller caller, AuditActor actor, CancellationToken cancellationToken)
    {
        if (caller.IsProviderScoped)
            throw new ForbiddenException("Provider administrators create drivers for their own provider only.");

        var user = await AmbulanceAccountFactory.StageAsync(dbContext, request.FirstName, request.LastName, request.Email,
            request.Phone, request.Password, AmbulanceRules.DriverRoleName, cancellationToken);
        AuditWriter.Stage(dbContext, actor, "AmbulanceDriverCreated", nameof(User), user.Id.ToString(), $"Hospital ambulance driver account {user.Email} created.");
        await dbContext.SaveChangesAsync(cancellationToken);
        return new DriverOptionDto(user.Id, AmbulanceMapper.FullName(user), user.Email, user.PhoneNumber, null, null, null, null);
    }

    // ------------------------------------------------------------------ helpers
    private static IQueryable<Ambulance> Scoped(IQueryable<Ambulance> source, AmbulanceCaller caller) =>
        caller.IsProviderScoped ? source.Where(a => a.ProviderId == caller.ProviderId) : source;

    private static void EnsureOwnership(Ambulance ambulance, AmbulanceCaller caller)
    {
        if (caller.IsProviderScoped && ambulance.ProviderId != caller.ProviderId)
            throw new ForbiddenException("You can only manage ambulances that belong to your own provider.");
    }

    private async Task<Ambulance> LoadAsync(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        var source = tracking ? dbContext.Ambulances : dbContext.Ambulances.AsNoTracking();
        return await source.Include(a => a.Provider).Include(a => a.DriverUser)
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Ambulance), id);
    }

    private Task<bool> HasActiveAssignmentAsync(Guid ambulanceId, CancellationToken cancellationToken) =>
        dbContext.AmbulanceAssignments.AnyAsync(a => a.AmbulanceId == ambulanceId && a.IsActive, cancellationToken);

    private async Task<Dictionary<Guid, AmbulanceAssignment>> ActiveAssignmentsAsync(IEnumerable<Guid> ambulanceIds, CancellationToken cancellationToken)
    {
        var ids = ambulanceIds.ToList();
        var rows = await dbContext.AmbulanceAssignments.AsNoTracking().Include(a => a.Request)
            .Where(a => a.IsActive && ids.Contains(a.AmbulanceId)).ToListAsync(cancellationToken);
        return rows.ToDictionary(a => a.AmbulanceId);
    }

    private async Task<AmbulanceDto> ToDtoAsync(Ambulance ambulance, AmbulanceViewOptions view, CancellationToken cancellationToken)
    {
        var active = await ActiveAssignmentsAsync([ambulance.Id], cancellationToken);
        return AmbulanceMapper.ToDto(ambulance, view, null, active.GetValueOrDefault(ambulance.Id));
    }

    private Task Publish(Ambulance ambulance) =>
        broadcaster.AmbulanceAvailabilityChangedAsync(new AmbulanceAvailabilityEventDto(ambulance.Id, ambulance.VehicleNumber, ambulance.Status, ambulance.IsActive));

    private async Task SaveAsync(CancellationToken cancellationToken, string conflictMessage)
    {
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            throw new ConflictException(conflictMessage);
        }
    }

    private static string NormalizeVehicleNumber(string? value)
    {
        var number = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (!VehicleNumberPattern.IsMatch(number))
            throw new RequestValidationException("Vehicle number must be 3-30 characters: letters, digits, spaces or hyphens.");
        return number;
    }

    private static AmbulanceType Defined(AmbulanceType type) =>
        Enum.IsDefined(type) ? type : throw new RequestValidationException("Select a valid ambulance type.");
}
