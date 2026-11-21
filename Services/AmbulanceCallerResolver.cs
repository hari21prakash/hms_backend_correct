using HospitalManagement.Api.Data;

namespace HospitalManagement.Api.Services;

/// <summary>Resolves the provider scope of the signed-in user from the database (never from the request).</summary>
public class AmbulanceCallerResolver(AppDbContext dbContext)
{
    public Task<AmbulanceCaller> ResolveAsync(Guid userId, CancellationToken cancellationToken) =>
        AmbulanceCaller.ResolveAsync(dbContext, userId, cancellationToken);

    /// <summary>The dispatch desk is for hospital staff only: provider-linked users are always refused, even if given the permission.</summary>
    public async Task<AmbulanceCaller> RequireHospitalStaffAsync(Guid userId, CancellationToken cancellationToken)
    {
        var caller = await ResolveAsync(userId, cancellationToken);
        if (caller.IsProviderScoped)
            throw new Exceptions.ForbiddenException("Ambulance dispatch is restricted to hospital staff.");
        return caller;
    }
}
