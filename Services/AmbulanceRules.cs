using System.Security.Cryptography;
using System.Text;
using HospitalManagement.Api.Data;
using HospitalManagement.Api.Exceptions;
using HospitalManagement.Api.Models;
using HospitalManagement.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace HospitalManagement.Api.Services;

/// <summary>
/// Who is calling. Provider-linked users (provider admins and provider drivers) are ALWAYS restricted to their own provider;
/// everyone else is hospital staff. Derived from the database, never from request data.
/// </summary>
public sealed record AmbulanceCaller(Guid UserId, Guid? ProviderId, ProviderUserRole? ProviderRole)
{
    public bool IsProviderScoped => ProviderId is not null;

    public static async Task<AmbulanceCaller> ResolveAsync(AppDbContext dbContext, Guid userId, CancellationToken cancellationToken)
    {
        var link = await dbContext.AmbulanceProviderUsers
            .AsNoTracking()
            .Where(item => item.UserId == userId && item.IsActive)
            .Select(item => new { item.ProviderId, item.Role })
            .FirstOrDefaultAsync(cancellationToken);
        return new AmbulanceCaller(userId, link?.ProviderId, link?.Role);
    }
}

public static class AmbulanceRules
{
    public const string DriverRoleName = "Ambulance Driver";
    public const string ProviderAdminRoleName = "Ambulance Provider Admin";

    /// <summary>How an arrival was recorded: by the server's GPS geofence check, or by the driver as the fallback.</summary>
    public const string ArrivalAuto = "Auto";
    public const string ArrivalManual = "Manual";

    /// <summary>Who confirmed pick-up or handover: the driver (fallback) or the hospital dispatch desk.</summary>
    public const string SourceDriver = "Driver";
    public const string SourceDispatcher = "Dispatcher";

    /// <summary>A server-side minimum gap between stored GPS fixes of one ambulance (protects the database from a chatty client).</summary>
    public static readonly TimeSpan MinLocationInterval = TimeSpan.FromSeconds(3);
    /// <summary>A fix whose device timestamp is older than this is discarded as stale.</summary>
    public static readonly TimeSpan MaxClientFixAge = TimeSpan.FromMinutes(2);
    /// <summary>The public page and the dispatch map call a position stale after this long without an update.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(30);

    private static readonly Dictionary<AmbulanceRequestStatus, AmbulanceRequestStatus[]> RequestTransitions = new()
    {
        [AmbulanceRequestStatus.Requested] = [AmbulanceRequestStatus.Accepted, AmbulanceRequestStatus.Cancelled, AmbulanceRequestStatus.Rejected],
        [AmbulanceRequestStatus.Accepted] = [AmbulanceRequestStatus.Assigned, AmbulanceRequestStatus.Cancelled, AmbulanceRequestStatus.Rejected],
        // Assigned -> Accepted happens when the driver declines: the request goes back to the dispatch queue.
        [AmbulanceRequestStatus.Assigned] = [AmbulanceRequestStatus.Accepted, AmbulanceRequestStatus.Dispatched, AmbulanceRequestStatus.Cancelled],
        [AmbulanceRequestStatus.Dispatched] = [AmbulanceRequestStatus.Arrived, AmbulanceRequestStatus.Cancelled],
        [AmbulanceRequestStatus.Arrived] = [AmbulanceRequestStatus.PatientPickedUp, AmbulanceRequestStatus.Cancelled],
        [AmbulanceRequestStatus.PatientPickedUp] = [AmbulanceRequestStatus.AtHospital],
        [AmbulanceRequestStatus.AtHospital] = [AmbulanceRequestStatus.Completed],
    };

    public static bool CanTransition(AmbulanceRequestStatus from, AmbulanceRequestStatus to) =>
        RequestTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);

    public static bool IsOpen(AmbulanceRequestStatus status) =>
        status is not (AmbulanceRequestStatus.Completed or AmbulanceRequestStatus.Cancelled or AmbulanceRequestStatus.Rejected);

    public static bool IsActive(AssignmentStatus status) =>
        status is AssignmentStatus.Pending or AssignmentStatus.Accepted or AssignmentStatus.EnRoute
            or AssignmentStatus.Arrived or AssignmentStatus.PickedUp;

    /// <summary>GPS is only recorded (and shown) while the trip is under way.</summary>
    public static bool IsTripActive(AssignmentStatus status) =>
        status is AssignmentStatus.EnRoute or AssignmentStatus.Arrived or AssignmentStatus.PickedUp;

    /// <summary>Changes the request status, validating the transition and recording history. The caller saves.</summary>
    public static void MoveRequest(AppDbContext dbContext, AmbulanceRequest request, AmbulanceRequestStatus to, Guid? actorUserId, string? note = null)
    {
        if (!CanTransition(request.Status, to))
            throw new ConflictException($"A request that is {request.Status} cannot move to {to}.");

        dbContext.AmbulanceStatusHistories.Add(new AmbulanceStatusHistory
        {
            Id = Guid.NewGuid(),
            Scope = StatusHistoryScope.Request,
            RequestId = request.Id,
            FromStatus = request.Status.ToString(),
            ToStatus = to.ToString(),
            ChangedByUserId = actorUserId,
            ChangedAt = DateTime.UtcNow,
            Note = Trim(note, 500),
        });
        request.Status = to;
    }

    /// <summary>Changes the ambulance availability and records history. The caller saves.</summary>
    public static void MoveAmbulance(AppDbContext dbContext, Ambulance ambulance, AmbulanceStatus to, Guid? actorUserId, string? note = null)
    {
        if (ambulance.Status == to) return;
        dbContext.AmbulanceStatusHistories.Add(new AmbulanceStatusHistory
        {
            Id = Guid.NewGuid(),
            Scope = StatusHistoryScope.Ambulance,
            AmbulanceId = ambulance.Id,
            FromStatus = ambulance.Status.ToString(),
            ToStatus = to.ToString(),
            ChangedByUserId = actorUserId,
            ChangedAt = DateTime.UtcNow,
            Note = Trim(note, 500),
        });
        ambulance.Status = to;
    }

    public static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        return text.Length <= max ? text : text[..max];
    }

    public static void ValidateCoordinates(double? latitude, double? longitude)
    {
        if (latitude is null || longitude is null ||
            double.IsNaN(latitude.Value) || double.IsInfinity(latitude.Value) ||
            double.IsNaN(longitude.Value) || double.IsInfinity(longitude.Value) ||
            latitude is < -90 or > 90 || longitude is < -180 or > 180)
            throw new RequestValidationException("Latitude must be between -90 and 90 and longitude between -180 and 180.");
    }

    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthKm = 6371.0088;
        static double Rad(double d) => d * Math.PI / 180.0;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return earthKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <summary>A rough straight-line estimate. Real road routing is not available, so it is labelled an estimate in the UI.</summary>
    public static int? EtaMinutes(double distanceKm, double? speedKmh)
    {
        var speed = speedKmh is >= 10 ? Math.Min(speedKmh.Value, 90) : 35;
        return (int)Math.Ceiling(distanceKm / speed * 60);
    }

    // ---------------------------------------------------------------- tracking token
    /// <summary>256 bits from the OS CSPRNG, base64url. Only the SHA-256 hash is ever stored.</summary>
    public static (string Token, string Hash) NewTrackingToken()
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (token, HashToken(token));
    }

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public static bool TokenMatches(string? token, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length is < 20 or > 100) return false;
        var supplied = Encoding.ASCII.GetBytes(HashToken(token.Trim()));
        var stored = Encoding.ASCII.GetBytes(storedHash);
        return supplied.Length == stored.Length && CryptographicOperations.FixedTimeEquals(supplied, stored);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
