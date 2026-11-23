using HospitalManagement.Api.Data;
using HospitalManagement.Api.DTOs;
using HospitalManagement.Api.Interfaces;
using HospitalManagement.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagement.Api.Services;

/// <summary>Builds fresh DTOs after a committed change and hands them to the broadcaster. Never throws into a request.</summary>
public class AmbulancePublisher(AppDbContext dbContext, IAmbulanceBroadcaster broadcaster, IAmbulanceRequestService requests)
{
    // Pushed events never carry GPS to the general dispatch group (locations use their own permission-gated group).
    public static readonly AmbulanceViewOptions DispatchView = new(IncludeLocation: false, IncludeDriver: true);

    public async Task RequestChangedAsync(Guid requestId, Guid? assignmentId = null, CancellationToken cancellationToken = default)
    {
        var request = await dbContext.AmbulanceRequests.AsNoTracking().Include(r => r.Patient)
            .SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null) return;

        var active = await dbContext.AmbulanceAssignments.AsNoTracking()
            .Include(a => a.Request).Include(a => a.Ambulance).ThenInclude(a => a.Provider).Include(a => a.DriverUser)
            .Where(a => a.RequestId == requestId && a.IsActive)
            .FirstOrDefaultAsync(cancellationToken);
        var latest = active ?? await dbContext.AmbulanceAssignments.AsNoTracking()
            .Include(a => a.Request).Include(a => a.Ambulance).ThenInclude(a => a.Provider).Include(a => a.DriverUser)
            .Where(a => a.RequestId == requestId).OrderByDescending(a => a.AssignedAt)
            .FirstOrDefaultAsync(cancellationToken);

        await broadcaster.RequestChangedAsync(
            AmbulanceMapper.ToRequestDto(request, active, null, DispatchView, latest),
            await requests.BuildPublicViewAsync(requestId, cancellationToken));

        var changedAssignment = active;
        if (assignmentId is { } id && (active is null || active.Id != id))
        {
            changedAssignment = await dbContext.AmbulanceAssignments.AsNoTracking()
                .Include(a => a.Request).Include(a => a.Ambulance).ThenInclude(a => a.Provider).Include(a => a.DriverUser)
                .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        }
        if (changedAssignment is not null)
            await broadcaster.AssignmentChangedAsync(AmbulanceMapper.ToAssignmentDto(changedAssignment, DispatchView));
    }

    public Task AvailabilityChangedAsync(Ambulance ambulance) =>
        broadcaster.AmbulanceAvailabilityChangedAsync(new AmbulanceAvailabilityEventDto(ambulance.Id, ambulance.VehicleNumber, ambulance.Status, ambulance.IsActive));
}
