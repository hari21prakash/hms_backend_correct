using System.ComponentModel.DataAnnotations;
using HospitalManagement.Api.Data;
using HospitalManagement.Api.DTOs;
using HospitalManagement.Api.Exceptions;
using HospitalManagement.Api.Interfaces;
using HospitalManagement.Api.Models;
using HospitalManagement.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagement.Api.Services;

/// <summary>Public booking and tracking. Anonymous callers get ONLY what <see cref="PublicTrackingDto"/> contains.</summary>
public class AmbulanceRequestService(AppDbContext dbContext, IAmbulanceBroadcaster broadcaster) : IAmbulanceRequestService
{
    public const string NotFoundMessage = "Tracking information was not found.";

    // A syntactically valid hash used so that "unknown request number" costs the same as "wrong token".
    private static readonly string DummyHash = AmbulanceRules.HashToken("not-a-real-token-for-timing-equalisation");

    public async Task<PublicBookingResultDto> CreateBookingAsync(CreateAmbulanceBookingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        AmbulanceRules.ValidateCoordinates(request.Latitude, request.Longitude);

        var address = (request.Address ?? string.Empty).Trim();
        if (address.Length == 0 || address.Length > 500)
            throw new RequestValidationException("Enter the accident address (up to 500 characters).");
        if (!Enum.IsDefined(request.EmergencyType))
            throw new RequestValidationException("Select a valid emergency type.");
        if (request.InjuredCount is < 1 or > 100)
            throw new RequestValidationException("Number of injured people must be between 1 and 100.");

        var phone = AmbulanceRules.Trim(request.ContactPhone, 30);
        if (phone is not null && !new PhoneAttribute().IsValid(phone))
            throw new RequestValidationException("The contact number is not valid.");
        var name = AmbulanceRules.Trim(request.RequesterName, 100);
        var description = AmbulanceRules.Trim(request.Description, 1000);
        if (request.Description is { Length: > 1000 } || request.RequesterName is { Length: > 100 })
            throw new RequestValidationException("The name or description is too long.");

        var (token, hash) = AmbulanceRules.NewTrackingToken();
        var number = await NextRequestNumberAsync(cancellationToken);
        var now = DateTime.UtcNow;

        var booking = new AmbulanceRequest
        {
            Id = Guid.NewGuid(),
            RequestNumber = number,
            TrackingTokenHash = hash,          // only the hash is stored
            Latitude = request.Latitude!.Value,
            Longitude = request.Longitude!.Value,
            Address = address,
            EmergencyType = request.EmergencyType,
            InjuredCount = request.InjuredCount,
            RequesterName = name,
            ContactPhone = phone,
            Description = description,
            Status = AmbulanceRequestStatus.Requested,
        };
        dbContext.AmbulanceRequests.Add(booking);
        dbContext.AmbulanceStatusHistories.Add(new AmbulanceStatusHistory
        {
            Id = Guid.NewGuid(), Scope = StatusHistoryScope.Request, RequestId = booking.Id,
            ToStatus = AmbulanceRequestStatus.Requested.ToString(), ChangedByUserId = null, ChangedAt = now, Note = "Public booking",
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        await broadcaster.RequestCreatedAsync(
            AmbulanceMapper.ToRequestDto(booking, null, null, new AmbulanceViewOptions(IncludeLocation: false, IncludeDriver: true)));

        // The token travels in the URL FRAGMENT: browsers never send it to servers, proxies or access logs.
        return new PublicBookingResultDto(number, token, $"/ambulance/track/{number}#t={token}", booking.Status, booking.CreatedAt);
    }

    public async Task<PublicTrackingDto> GetPublicTrackingAsync(string requestNumber, string? token, CancellationToken cancellationToken)
    {
        var id = await ValidateTrackingAccessAsync(requestNumber, token, cancellationToken);
        if (id is null) throw new NotFoundException(NotFoundMessage);   // one message for "unknown" and "wrong token"
        return await BuildPublicViewAsync(id.Value, cancellationToken);
    }

    public async Task<Guid?> ValidateTrackingAccessAsync(string requestNumber, string? token, CancellationToken cancellationToken)
    {
        var number = (requestNumber ?? string.Empty).Trim().ToUpperInvariant();
        if (number.Length is 0 or > 30)
        {
            AmbulanceRules.TokenMatches(token, DummyHash);
            return null;
        }

        var row = await dbContext.AmbulanceRequests.AsNoTracking()
            .Where(r => r.RequestNumber == number)
            .Select(r => new { r.Id, r.TrackingTokenHash })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            AmbulanceRules.TokenMatches(token, DummyHash);
            return null;
        }

        return AmbulanceRules.TokenMatches(token, row.TrackingTokenHash) ? row.Id : null;
    }

    public async Task<PublicTrackingDto> BuildPublicViewAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var request = await dbContext.AmbulanceRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken)
            ?? throw new NotFoundException(NotFoundMessage);

        var assignment = await dbContext.AmbulanceAssignments.AsNoTracking()
            .Include(a => a.Ambulance)
            .Where(a => a.RequestId == requestId && (a.IsActive || a.Status == AssignmentStatus.Completed))
            .OrderByDescending(a => a.AssignedAt)
            .FirstOrDefaultAsync(cancellationToken);

        PublicLocationDto? location = null;
        double? distanceKm = null;
        int? eta = null;
        var trail = new List<PublicTrailPointDto>();
        var trackingActive = assignment is { IsActive: true } && AmbulanceRules.IsTripActive(assignment.Status);

        if (trackingActive && assignment!.Ambulance is { CurrentLatitude: { } lat, CurrentLongitude: { } lon, LastLocationAt: { } at } ambulance
            && at >= (assignment.StartedAt ?? DateTime.MinValue))   // ignore a position left over from an earlier trip
        {
            location = new PublicLocationDto(lat, lon, ambulance.LocationAccuracyMeters, ambulance.LocationSpeedKmh, at);
            if (assignment.Status == AssignmentStatus.EnRoute)      // heading to the scene: the destination is the request
            {
                distanceKm = Math.Round(AmbulanceRules.DistanceKm(lat, lon, request.Latitude, request.Longitude), 2);
                eta = AmbulanceRules.EtaMinutes(distanceKm.Value, ambulance.LocationSpeedKmh);
            }

            var points = await dbContext.AmbulanceLocationHistories.AsNoTracking()
                .Where(h => h.AssignmentId == assignment.Id)
                .OrderByDescending(h => h.ReceivedAt).Take(80)
                .Select(h => new PublicTrailPointDto(h.Latitude, h.Longitude))
                .ToListAsync(cancellationToken);
            points.Reverse();
            var step = Math.Max(1, points.Count / 40);
            trail = points.Where((_, index) => index % step == 0).ToList();
        }

        var timeline = (await dbContext.AmbulanceStatusHistories.AsNoTracking()
                .Where(h => h.RequestId == requestId && h.Scope == StatusHistoryScope.Request)
                .OrderBy(h => h.ChangedAt)
                .Select(h => new { h.ToStatus, h.ChangedAt })
                .ToListAsync(cancellationToken))
            .Where(h => Enum.TryParse<AmbulanceRequestStatus>(h.ToStatus, out _))
            .Select(h => new PublicTimelineItemDto(Enum.Parse<AmbulanceRequestStatus>(h.ToStatus), h.ChangedAt))
            .ToList();

        return new PublicTrackingDto(
            request.RequestNumber, request.Status, AmbulanceMapper.StatusLabel(request.Status), request.CreatedAt, DateTime.UtcNow,
            new PublicPointDto(request.Latitude, request.Longitude, request.Address),
            assignment is null ? null : AmbulanceMapper.AssignmentLabel(assignment.Status),
            assignment?.Ambulance.VehicleNumber,
            assignment?.Ambulance.Type,
            trackingActive, location, distanceKm, eta, trail, timeline);
    }

    private async Task<string> NextRequestNumberAsync(CancellationToken cancellationToken)
    {
        long sequenceNumber;
        if (dbContext.Database.IsRelational())
        {
            sequenceNumber = await dbContext.Database
                .SqlQueryRaw<long>($"SELECT nextval('{DbSequences.AmbulanceRequestNumber}') AS \"Value\"")
                .SingleAsync(cancellationToken);
        }
        else
        {
            sequenceNumber = await dbContext.AmbulanceRequests.LongCountAsync(cancellationToken) + 1;
        }

        return $"AMB-{DateTime.UtcNow.Year}-{sequenceNumber:D6}";
    }
}
