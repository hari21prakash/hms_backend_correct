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
/// The hospital dispatch desk. Assignment is race-safe: the unique partial indexes on AmbulanceAssignments (one active row per
/// request and per ambulance) make double dispatch impossible even if two dispatchers click at the same moment.
/// </summary>
public class AmbulanceDispatchService(AppDbContext dbContext, AmbulancePublisher publisher) : IAmbulanceDispatchService
{
    public async Task<IReadOnlyList<DispatchRequestDto>> ListRequestsAsync(DispatchRequestQuery query, AmbulanceViewOptions view, CancellationToken cancellationToken)
    {
        var source = dbContext.AmbulanceRequests.AsNoTracking().Include(r => r.Patient).AsQueryable();
        if (query.Status is { } status) source = source.Where(r => r.Status == status);
        if (query.Open == true)
            source = source.Where(r => r.Status != AmbulanceRequestStatus.Completed && r.Status != AmbulanceRequestStatus.Cancelled && r.Status != AmbulanceRequestStatus.Rejected);
        else if (query.Open == false)
            source = source.Where(r => r.Status == AmbulanceRequestStatus.Completed || r.Status == AmbulanceRequestStatus.Cancelled || r.Status == AmbulanceRequestStatus.Rejected);

        var requests = await source.OrderByDescending(r => r.CreatedAt).Take(200).ToListAsync(cancellationToken);
        var ids = requests.Select(r => r.Id).ToList();
        var active = await dbContext.AmbulanceAssignments.AsNoTracking()
            .Include(a => a.Request).Include(a => a.Ambulance).ThenInclude(a => a.Provider).Include(a => a.DriverUser)
            .Where(a => a.IsActive && ids.Contains(a.RequestId)).ToListAsync(cancellationToken);
        var byRequest = active.ToDictionary(a => a.RequestId);

        return requests.Select(r => AmbulanceMapper.ToRequestDto(r, byRequest.GetValueOrDefault(r.Id), null, view)).ToList();
    }

    public async Task<DispatchRequestDto> GetRequestAsync(Guid id, AmbulanceViewOptions view, CancellationToken cancellationToken) =>
        await BuildAsync(id, view, includeHistory: true, cancellationToken);

    public async Task<DispatchRequestDto> AcceptAsync(Guid id, AuditActor actor, AmbulanceViewOptions view, CancellationToken cancellationToken)
    {
        var request = await LoadTrackedAsync(id, cancellationToken);
        AmbulanceRules.MoveRequest(dbContext, request, AmbulanceRequestStatus.Accepted, actor.UserId);
        AuditWriter.Stage(dbContext, actor, "AmbulanceRequestAccepted", nameof(AmbulanceRequest), id.ToString(), $"Request {request.RequestNumber} accepted.");
        await dbContext.SaveChangesAsync(cancellationToken);
        await publisher.RequestChangedAsync(id, null, cancellationToken);
        return await BuildAsync(id, view, true, cancellationToken);
    }

    public async Task<DispatchRequestDto> RejectAsync(Guid id, string? reason, AuditActor actor, AmbulanceViewOptions view, CancellationToken cancellationToken)
    {
        var text = RequireReason(reason, "rejecting");
        var request = await LoadTrackedAsync(id, cancellationToken);
        AmbulanceRules.MoveRequest(dbContext, request, AmbulanceRequestStatus.Rejected, actor.UserId, text);
        request.StatusReason = text;
        AuditWriter.Stage(dbContext, actor, "AmbulanceRequestRejected", nameof(AmbulanceRequest), id.ToString(), $"Request {request.RequestNumber} rejected: {text}");
        await dbContext.SaveChangesAsync(cancellationToken);
        await publisher.RequestChangedAsync(id, null, cancellationToken);
        return await BuildAsync(id, view, true, cancellationToken);
    }

    public async Task<DispatchRequestDto> CancelAsync(Guid id, string? reason, AuditActor actor, AmbulanceViewOptions view, CancellationToken cancellationToken)
    {
        var text = RequireReason(reason, "cancelling");
        await using var transaction = await BeginAsync(cancellationToken);

        var request = await LoadTrackedAsync(id, cancellationToken);
        AmbulanceRules.MoveRequest(dbContext, request, AmbulanceRequestStatus.Cancelled, actor.UserId, text);
        request.StatusReason = text;

        // Stop the trip: the assignment ends, the ambulance is freed, and the driver's page stops tracking.
        var assignment = await dbContext.AmbulanceAssignments.Include(a => a.Ambulance)
            .SingleOrDefaultAsync(a => a.RequestId == id && a.IsActive, cancellationToken);
        Ambulance? freed = null;
        if (assignment is not null)
        {
            assignment.Status = AssignmentStatus.Cancelled;
            assignment.IsActive = false;
            assignment.EndedAt = DateTime.UtcNow;
            AmbulanceRules.MoveAmbulance(dbContext, assignment.Ambulance, AmbulanceStatus.Available, actor.UserId, $"Request {request.RequestNumber} cancelled");
            freed = assignment.Ambulance;
        }

        AuditWriter.Stage(dbContext, actor, "AmbulanceRequestCancelled", nameof(AmbulanceRequest), id.ToString(), $"Request {request.RequestNumber} cancelled: {text}");
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);

        await publisher.RequestChangedAsync(id, assignment?.Id, cancellationToken);
        if (freed is not null) await publisher.AvailabilityChangedAsync(freed);
        return await BuildAsync(id, view, true, cancellationToken);
    }

    public async Task<DispatchRequestDto> AssignAsync(Guid id, Guid ambulanceId, AuditActor actor, AmbulanceViewOptions view, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAsync(cancellationToken);

        var request = await LoadTrackedAsync(id, cancellationToken);
        if (request.Status is not (AmbulanceRequestStatus.Requested or AmbulanceRequestStatus.Accepted))
            throw new ConflictException($"A request that is {request.Status} cannot be assigned an ambulance.");
        if (await dbContext.AmbulanceAssignments.AnyAsync(a => a.RequestId == id && a.IsActive, cancellationToken))
            throw new ConflictException("This request already has an active ambulance assignment.");

        var ambulance = await dbContext.Ambulances.Include(a => a.Provider).Include(a => a.DriverUser)
            .SingleOrDefaultAsync(a => a.Id == ambulanceId, cancellationToken)
            ?? throw new NotFoundException(nameof(Ambulance), ambulanceId);

        // Eligibility covers: active vehicle, Available status, a driver, AND (for external vehicles) an APPROVED provider.
        var busy = await dbContext.AmbulanceAssignments.AnyAsync(a => a.AmbulanceId == ambulanceId && a.IsActive, cancellationToken);
        var (eligible, reason) = AmbulanceMapper.Eligibility(ambulance, busy);
        if (!eligible)
            throw new ConflictException($"Ambulance {ambulance.VehicleNumber} cannot be dispatched: {reason}");

        if (request.Status == AmbulanceRequestStatus.Requested)
            AmbulanceRules.MoveRequest(dbContext, request, AmbulanceRequestStatus.Accepted, actor.UserId, "Accepted by dispatch");
        AmbulanceRules.MoveRequest(dbContext, request, AmbulanceRequestStatus.Assigned, actor.UserId, $"Ambulance {ambulance.VehicleNumber}");

        var assignment = new AmbulanceAssignment
        {
            Id = Guid.NewGuid(),
            RequestId = id,
            AmbulanceId = ambulanceId,
            DriverUserId = ambulance.DriverUserId!.Value,    // snapshot of the driver at assignment time
            Status = AssignmentStatus.Pending,
            IsActive = true,
            AssignedByUserId = actor.UserId,
            AssignedAt = DateTime.UtcNow,
        };
        dbContext.AmbulanceAssignments.Add(assignment);
        AmbulanceRules.MoveAmbulance(dbContext, ambulance, AmbulanceStatus.Reserved, actor.UserId, $"Assigned to {request.RequestNumber}");
        AuditWriter.Stage(dbContext, actor, "AmbulanceAssigned", nameof(AmbulanceAssignment), assignment.Id.ToString(),
            $"Ambulance {ambulance.VehicleNumber} assigned to request {request.RequestNumber}.");

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // The database refused a second active assignment for this request or this ambulance.
            throw new ConflictException("This ambulance or this request was just assigned by someone else. Refresh and choose again.");
        }

        await publisher.RequestChangedAsync(id, assignment.Id, cancellationToken);
        await publisher.AvailabilityChangedAsync(ambulance);
        return await BuildAsync(id, view, true, cancellationToken);
    }

    public async Task<DispatchRequestDto> CompleteAsync(Guid id, AuditActor actor, AmbulanceViewOptions view, CancellationToken cancellationToken)
    {
        var request = await LoadTrackedAsync(id, cancellationToken);
        AmbulanceRules.MoveRequest(dbContext, request, AmbulanceRequestStatus.Completed, actor.UserId);
        AuditWriter.Stage(dbContext, actor, "AmbulanceRequestCompleted", nameof(AmbulanceRequest), id.ToString(), $"Request {request.RequestNumber} completed.");
        await dbContext.SaveChangesAsync(cancellationToken);
        await publisher.RequestChangedAsync(id, null, cancellationToken);
        return await BuildAsync(id, view, true, cancellationToken);
    }

    public async Task<DispatchRequestDto> LinkPatientAsync(Guid id, Guid patientId, AuditActor actor, AmbulanceViewOptions view, CancellationToken cancellationToken)
    {
        var request = await LoadTrackedAsync(id, cancellationToken);
        if (request.Status is not (AmbulanceRequestStatus.PatientPickedUp or AmbulanceRequestStatus.AtHospital or AmbulanceRequestStatus.Completed))
            throw new ConflictException("A patient can be linked once the patient has been picked up.");

        // Validate the referenced patient first: an unknown patient id is a 404 whatever state the request is in,
        // and must never be reported as a "different patient" conflict.
        var patient = await dbContext.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == patientId, cancellationToken)
            ?? throw new NotFoundException(nameof(Patient), patientId);

        if (request.PatientId is { } existing && existing != patientId)
            throw new ConflictException("This request is already linked to a different patient.");

        // Idempotent: linking the same patient again changes nothing (no duplicate records, no duplicate audit rows).
        if (request.PatientId != patientId)
        {
            request.PatientId = patientId;
            request.PatientLinkedByUserId = actor.UserId;
            request.PatientLinkedAt = DateTime.UtcNow;
            AuditWriter.Stage(dbContext, actor, "AmbulanceRequestPatientLinked", nameof(AmbulanceRequest), id.ToString(),
                $"Request {request.RequestNumber} linked to patient {patient.PatientNumber}.");
            await dbContext.SaveChangesAsync(cancellationToken);
            await publisher.RequestChangedAsync(id, null, cancellationToken);
        }

        return await BuildAsync(id, view, true, cancellationToken);
    }

    public async Task<IReadOnlyList<DispatchAssignmentDto>> GetActiveAsync(AmbulanceViewOptions view, CancellationToken cancellationToken)
    {
        var rows = await dbContext.AmbulanceAssignments.AsNoTracking()
            .Include(a => a.Request).Include(a => a.Ambulance).ThenInclude(a => a.Provider).Include(a => a.DriverUser)
            .Where(a => a.IsActive).OrderBy(a => a.AssignedAt).ToListAsync(cancellationToken);
        return rows.Select(a => AmbulanceMapper.ToAssignmentDto(a, view)).ToList();
    }

    public async Task<IReadOnlyList<AmbulanceDto>> GetFleetAsync(AmbulanceViewOptions view, CancellationToken cancellationToken)
    {
        var ambulances = await dbContext.Ambulances.AsNoTracking().Include(a => a.Provider).Include(a => a.DriverUser)
            .Where(a => a.IsActive).OrderBy(a => a.VehicleNumber).ToListAsync(cancellationToken);
        var ids = ambulances.Select(a => a.Id).ToList();
        var active = (await dbContext.AmbulanceAssignments.AsNoTracking().Include(a => a.Request)
            .Where(a => a.IsActive && ids.Contains(a.AmbulanceId)).ToListAsync(cancellationToken))
            .ToDictionary(a => a.AmbulanceId);
        return ambulances.Select(a => AmbulanceMapper.ToDto(a, view, null, active.GetValueOrDefault(a.Id))).ToList();
    }

    // ------------------------------------------------------------------ helpers
    private static string RequireReason(string? reason, string action)
    {
        var text = AmbulanceRules.Trim(reason, 500);
        return text ?? throw new RequestValidationException($"A reason is required when {action} a request.");
    }

    private async Task<AmbulanceRequest> LoadTrackedAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.AmbulanceRequests.SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
        ?? throw new NotFoundException(nameof(AmbulanceRequest), id);

    private async Task<DispatchRequestDto> BuildAsync(Guid id, AmbulanceViewOptions view, bool includeHistory, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var request = await dbContext.AmbulanceRequests.AsNoTracking().Include(r => r.Patient)
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(AmbulanceRequest), id);
        var active = await dbContext.AmbulanceAssignments.AsNoTracking()
            .Include(a => a.Request).Include(a => a.Ambulance).ThenInclude(a => a.Provider).Include(a => a.DriverUser)
            .Where(a => a.RequestId == id && a.IsActive).FirstOrDefaultAsync(cancellationToken);

        IReadOnlyList<StatusHistoryItemDto>? history = null;
        if (includeHistory)
        {
            history = (await dbContext.AmbulanceStatusHistories.AsNoTracking().Include(h => h.ChangedByUser)
                    .Where(h => h.RequestId == id).OrderBy(h => h.ChangedAt).ToListAsync(cancellationToken))
                .Select(h => new StatusHistoryItemDto(h.FromStatus, h.ToStatus, h.ChangedAt,
                    h.ChangedByUser is null ? null : AmbulanceMapper.FullName(h.ChangedByUser), h.Note))
                .ToList();
        }

        return AmbulanceMapper.ToRequestDto(request, active, history, view);
    }

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginAsync(CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational() ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
}
