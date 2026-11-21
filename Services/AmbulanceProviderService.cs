using System.ComponentModel.DataAnnotations;
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
/// External provider registry. Hospital administrators (ambulances.providers.manage) manage every provider and decide approvals.
/// Provider administrators (provider-linked) can only see and edit THEIR OWN provider and can never approve anything.
/// </summary>
public class AmbulanceProviderService(AppDbContext dbContext) : IAmbulanceProviderService
{
    public async Task<IReadOnlyList<ProviderDto>> ListAsync(AmbulanceCaller caller, bool canManageProviders, CancellationToken cancellationToken)
    {
        RequireAccess(caller, canManageProviders);
        var source = dbContext.AmbulanceProviders.AsNoTracking().Include(p => p.ReviewedByUser).AsQueryable();
        if (caller.IsProviderScoped) source = source.Where(p => p.Id == caller.ProviderId);

        var providers = await source.OrderBy(p => p.Name).ToListAsync(cancellationToken);
        var ids = providers.Select(p => p.Id).ToList();
        var counts = await CountsAsync(ids, cancellationToken);
        return providers.Select(p => ToDto(p, counts)).ToList();
    }

    public async Task<ProviderDto> GetAsync(Guid id, AmbulanceCaller caller, bool canManageProviders, CancellationToken cancellationToken)
    {
        RequireAccess(caller, canManageProviders);
        EnsureOwn(id, caller);
        var provider = await LoadAsync(id, tracking: false, cancellationToken);
        return ToDto(provider, await CountsAsync([id], cancellationToken));
    }

    public async Task<ProviderDto> CreateAsync(SaveProviderRequest request, AmbulanceCaller caller, AuditActor actor, CancellationToken cancellationToken)
    {
        if (caller.IsProviderScoped)
            throw new ForbiddenException("Only hospital administrators can register providers.");

        var provider = new AmbulanceProvider { Id = Guid.NewGuid(), Status = ProviderStatus.Pending };
        Apply(provider, request);
        await EnsureUniqueAsync(null, provider.Name, provider.LicenseNumber, cancellationToken);
        dbContext.AmbulanceProviders.Add(provider);

        var anyAdminField = new[] { request.AdminFirstName, request.AdminLastName, request.AdminEmail, request.AdminPassword }
            .Any(value => !string.IsNullOrWhiteSpace(value));
        if (anyAdminField)
        {
            if (string.IsNullOrWhiteSpace(request.AdminFirstName) || string.IsNullOrWhiteSpace(request.AdminLastName) ||
                string.IsNullOrWhiteSpace(request.AdminEmail) || string.IsNullOrWhiteSpace(request.AdminPassword))
                throw new RequestValidationException("To create the provider's administrator login, provide first name, last name, email and password.");

            var admin = await AmbulanceAccountFactory.StageAsync(dbContext, request.AdminFirstName, request.AdminLastName,
                request.AdminEmail, request.Phone, request.AdminPassword, AmbulanceRules.ProviderAdminRoleName, cancellationToken);
            dbContext.AmbulanceProviderUsers.Add(new AmbulanceProviderUser
            {
                Id = Guid.NewGuid(), ProviderId = provider.Id, UserId = admin.Id, Role = ProviderUserRole.ProviderAdmin, IsActive = true,
            });
        }

        AuditWriter.Stage(dbContext, actor, "AmbulanceProviderRegistered", nameof(AmbulanceProvider), provider.Id.ToString(),
            $"Provider '{provider.Name}' registered (pending approval).");
        await SaveAsync(cancellationToken);
        return ToDto(await LoadAsync(provider.Id, false, cancellationToken), await CountsAsync([provider.Id], cancellationToken));
    }

    public async Task<ProviderDto> UpdateAsync(Guid id, SaveProviderRequest request, AmbulanceCaller caller, bool canManageProviders, AuditActor actor, CancellationToken cancellationToken)
    {
        RequireAccess(caller, canManageProviders);
        EnsureOwn(id, caller);
        var provider = await LoadAsync(id, tracking: true, cancellationToken);

        Apply(provider, request);     // details only: status, approval and ownership are never changed here
        await EnsureUniqueAsync(id, provider.Name, provider.LicenseNumber, cancellationToken);
        AuditWriter.Stage(dbContext, actor, "AmbulanceProviderUpdated", nameof(AmbulanceProvider), id.ToString(), $"Provider '{provider.Name}' details updated.");
        await SaveAsync(cancellationToken);
        return ToDto(provider, await CountsAsync([id], cancellationToken));
    }

    public async Task<ProviderDto> SetApprovalAsync(Guid id, ProviderApprovalRequest request, AmbulanceCaller caller, AuditActor actor, CancellationToken cancellationToken)
    {
        // Self-approval is impossible: provider-linked users can never reach this.
        if (caller.IsProviderScoped)
            throw new ForbiddenException("Only hospital administrators can approve, reject or suspend providers.");
        if (!Enum.IsDefined(request.Decision))
            throw new RequestValidationException("Select a valid decision.");

        var provider = await LoadAsync(id, tracking: true, cancellationToken);
        var reason = AmbulanceRules.Trim(request.Reason, 500);

        var (from, to) = (provider.Status, request.Decision switch
        {
            ProviderDecision.Approve when provider.Status == ProviderStatus.Pending => ProviderStatus.Approved,
            ProviderDecision.Reject when provider.Status == ProviderStatus.Pending => ProviderStatus.Rejected,
            ProviderDecision.Suspend when provider.Status == ProviderStatus.Approved => ProviderStatus.Suspended,
            ProviderDecision.Reinstate when provider.Status == ProviderStatus.Suspended => ProviderStatus.Approved,
            _ => throw new ConflictException($"A provider that is {provider.Status} cannot take the decision '{request.Decision}'."),
        });
        if (to is ProviderStatus.Rejected or ProviderStatus.Suspended && reason is null)
            throw new RequestValidationException("A reason is required to reject or suspend a provider.");

        provider.Status = to;
        provider.StatusReason = reason;
        provider.ReviewedByUserId = caller.UserId;
        provider.ReviewedAt = DateTime.UtcNow;
        AuditWriter.Stage(dbContext, actor, "AmbulanceProviderStatusChanged", nameof(AmbulanceProvider), id.ToString(),
            $"Provider '{provider.Name}': {from} -> {to}." + (reason is null ? string.Empty : $" Reason: {reason}"));
        await dbContext.SaveChangesAsync(cancellationToken);

        provider = await LoadAsync(id, false, cancellationToken);
        return ToDto(provider, await CountsAsync([id], cancellationToken));
    }

    public async Task<IReadOnlyList<DriverOptionDto>> ListDriversAsync(Guid providerId, AmbulanceCaller caller, bool canManageProviders, CancellationToken cancellationToken)
    {
        RequireAccess(caller, canManageProviders);
        EnsureOwn(providerId, caller);
        var provider = await LoadAsync(providerId, tracking: false, cancellationToken);

        var links = await dbContext.AmbulanceProviderUsers.AsNoTracking().Include(item => item.User)
            .Where(item => item.ProviderId == providerId && item.Role == ProviderUserRole.Driver && item.IsActive)
            .ToListAsync(cancellationToken);
        var ids = links.Select(item => item.UserId).ToList();
        var ambulances = await dbContext.Ambulances.AsNoTracking()
            .Where(a => a.DriverUserId != null && ids.Contains(a.DriverUserId.Value))
            .ToDictionaryAsync(a => a.DriverUserId!.Value, cancellationToken);

        return links.Select(link =>
        {
            ambulances.TryGetValue(link.UserId, out var ambulance);
            return new DriverOptionDto(link.UserId, AmbulanceMapper.FullName(link.User), link.User.Email, link.User.PhoneNumber,
                providerId, provider.Name, ambulance?.Id, ambulance?.VehicleNumber);
        }).ToList();
    }

    public async Task<DriverOptionDto> CreateDriverAsync(
        Guid providerId, CreateAmbulanceDriverRequest request, AmbulanceCaller caller, bool canManageProviders, AuditActor actor, CancellationToken cancellationToken)
    {
        RequireAccess(caller, canManageProviders);
        EnsureOwn(providerId, caller);
        var provider = await LoadAsync(providerId, tracking: false, cancellationToken);
        if (provider.Status == ProviderStatus.Rejected)
            throw new ConflictException("Drivers cannot be added to a rejected provider.");

        var user = await AmbulanceAccountFactory.StageAsync(dbContext, request.FirstName, request.LastName, request.Email,
            request.Phone, request.Password, AmbulanceRules.DriverRoleName, cancellationToken);
        dbContext.AmbulanceProviderUsers.Add(new AmbulanceProviderUser
        {
            Id = Guid.NewGuid(), ProviderId = providerId, UserId = user.Id, Role = ProviderUserRole.Driver, IsActive = true,
        });
        AuditWriter.Stage(dbContext, actor, "AmbulanceDriverCreated", nameof(User), user.Id.ToString(),
            $"Driver account {user.Email} created for provider '{provider.Name}'.");
        await SaveAsync(cancellationToken);
        return new DriverOptionDto(user.Id, AmbulanceMapper.FullName(user), user.Email, user.PhoneNumber, providerId, provider.Name, null, null);
    }

    // ------------------------------------------------------------------ helpers
    /// <summary>Hospital staff need ambulances.providers.manage; provider-linked callers must be their provider's ADMINISTRATOR.</summary>
    private static void RequireAccess(AmbulanceCaller caller, bool canManageProviders)
    {
        if (caller.IsProviderScoped)
        {
            if (caller.ProviderRole != ProviderUserRole.ProviderAdmin)
                throw new ForbiddenException("Only the provider administrator can manage the provider.");
            return;
        }
        if (!canManageProviders)
            throw new ForbiddenException("You do not have permission to manage ambulance providers.");
    }

    private static void EnsureOwn(Guid providerId, AmbulanceCaller caller)
    {
        if (caller.IsProviderScoped && caller.ProviderId != providerId)
            throw new ForbiddenException("You can only manage your own provider organisation.");
    }

    private async Task<AmbulanceProvider> LoadAsync(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        var source = tracking ? dbContext.AmbulanceProviders : dbContext.AmbulanceProviders.AsNoTracking();
        return await source.Include(p => p.ReviewedByUser).SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(AmbulanceProvider), id);
    }

    private static void Apply(AmbulanceProvider provider, SaveProviderRequest request)
    {
        var email = (request.Email ?? string.Empty).Trim();
        var phone = (request.Phone ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.ContactPerson) ||
            string.IsNullOrWhiteSpace(request.Address) || string.IsNullOrWhiteSpace(request.LicenseNumber))
            throw new RequestValidationException("Name, contact person, address and licence number are required.");
        if (!new EmailAddressAttribute().IsValid(email))
            throw new RequestValidationException("A valid contact email is required.");
        if (phone.Length == 0 || !new PhoneAttribute().IsValid(phone))
            throw new RequestValidationException("A valid contact phone number is required.");

        provider.Name = request.Name.Trim();
        provider.ContactPerson = request.ContactPerson.Trim();
        provider.Email = email;
        provider.Phone = phone;
        provider.Address = request.Address.Trim();
        provider.LicenseNumber = request.LicenseNumber.Trim().ToUpperInvariant();
        provider.Notes = AmbulanceRules.Trim(request.Notes, 1000);
    }

    private async Task EnsureUniqueAsync(Guid? selfId, string name, string license, CancellationToken cancellationToken)
    {
        var lowered = name.ToLower();
        if (await dbContext.AmbulanceProviders.AnyAsync(p => p.Id != selfId && p.Name.ToLower() == lowered, cancellationToken))
            throw new ConflictException("A provider with this name already exists.");
        if (await dbContext.AmbulanceProviders.AnyAsync(p => p.Id != selfId && p.LicenseNumber == license, cancellationToken))
            throw new ConflictException("A provider with this licence number already exists.");
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            throw new ConflictException("A provider with this name or licence number, or a login with this email, already exists.");
        }
    }

    private sealed record Counts(int Ambulances, int Available, int Drivers);

    private async Task<Dictionary<Guid, Counts>> CountsAsync(List<Guid> providerIds, CancellationToken cancellationToken)
    {
        var ambulances = await dbContext.Ambulances.AsNoTracking()
            .Where(a => a.ProviderId != null && providerIds.Contains(a.ProviderId.Value))
            .Select(a => new { ProviderId = a.ProviderId!.Value, Available = a.IsActive && a.Status == AmbulanceStatus.Available })
            .ToListAsync(cancellationToken);
        var drivers = await dbContext.AmbulanceProviderUsers.AsNoTracking()
            .Where(u => providerIds.Contains(u.ProviderId) && u.Role == ProviderUserRole.Driver && u.IsActive)
            .Select(u => u.ProviderId).ToListAsync(cancellationToken);

        return providerIds.ToDictionary(
            id => id,
            id => new Counts(
                ambulances.Count(a => a.ProviderId == id),
                ambulances.Count(a => a.ProviderId == id && a.Available),
                drivers.Count(d => d == id)));
    }

    private static ProviderDto ToDto(AmbulanceProvider p, Dictionary<Guid, Counts> counts)
    {
        counts.TryGetValue(p.Id, out var c);
        return new ProviderDto(p.Id, p.Name, p.ContactPerson, p.Email, p.Phone, p.Address, p.LicenseNumber, p.Notes,
            p.Status, p.StatusReason, p.ReviewedAt, p.ReviewedByUser is null ? null : AmbulanceMapper.FullName(p.ReviewedByUser),
            p.CreatedAt, c?.Ambulances ?? 0, c?.Available ?? 0, c?.Drivers ?? 0);
    }
}
