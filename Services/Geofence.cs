namespace HospitalManagement.Api.Services;

/// <summary>One stored GPS fix. <c>At</c> is the SERVER receipt time, never the device clock.</summary>
public readonly record struct GeoFix(double Latitude, double Longitude, double? AccuracyMeters, DateTime At);

public sealed record GeofenceSettings(double RadiusMeters, double MaxAccuracyMeters, TimeSpan Dwell, double MaxPlausibleSpeedKmh);

public enum GeofenceOutcome
{
    Arrived,
    /// <summary>There is no fix to judge.</summary>
    NoFix,
    /// <summary>The newest fix does not report an accuracy, so it cannot be trusted.</summary>
    AccuracyUnknown,
    /// <summary>The newest fix is less accurate than the configured limit.</summary>
    AccuracyTooLow,
    /// <summary>The newest fix is outside the radius.</summary>
    OutsideZone,
    /// <summary>Inside the radius, but not yet for long enough (or the run was broken by a poor fix, an exit or a GPS jump).</summary>
    DwellNotMet,
}

public sealed record GeofenceResult(GeofenceOutcome Outcome, double? DistanceMeters, DateTime? InsideSince, GeoFix? Fix)
{
    public bool Arrived => Outcome == GeofenceOutcome.Arrived;
}

/// <summary>
/// Pure arrival detection (no database, no clock). A vehicle has ARRIVED only when its newest fix and an unbroken run of earlier
/// fixes are all (1) accurate enough, (2) inside the radius and (3) free of impossible GPS jumps, and the run lasts at least the
/// dwell time. One stray fix, leaving the zone, or a teleporting fix restarts the dwell.
/// </summary>
public static class Geofence
{
    /// <summary>Movement below this between two fixes is normal GPS jitter and is never treated as a jump.</summary>
    public const double JitterMeters = 20;

    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2) =>
        AmbulanceRules.DistanceKm(lat1, lon1, lat2, lon2) * 1000.0;

    /// <param name="fixesOldestFirst">The fixes of ONE stage of ONE assignment, oldest first, newest (just received) last.</param>
    public static GeofenceResult Evaluate(IReadOnlyList<GeoFix> fixesOldestFirst, double targetLatitude, double targetLongitude, GeofenceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(fixesOldestFirst);
        ArgumentNullException.ThrowIfNull(settings);
        if (fixesOldestFirst.Count == 0) return new GeofenceResult(GeofenceOutcome.NoFix, null, null, null);

        var newest = fixesOldestFirst[^1];
        var distance = DistanceMeters(newest.Latitude, newest.Longitude, targetLatitude, targetLongitude);
        if (!double.IsFinite(distance)) return new GeofenceResult(GeofenceOutcome.NoFix, null, null, newest);
        if (newest.AccuracyMeters is not { } accuracy || !double.IsFinite(accuracy))
            return new GeofenceResult(GeofenceOutcome.AccuracyUnknown, distance, null, newest);
        if (accuracy > settings.MaxAccuracyMeters)
            return new GeofenceResult(GeofenceOutcome.AccuracyTooLow, distance, null, newest);
        if (distance > settings.RadiusMeters)
            return new GeofenceResult(GeofenceOutcome.OutsideZone, distance, null, newest);

        // Walk backwards while the earlier fixes are also good, inside and reached without an impossible jump.
        var start = fixesOldestFirst.Count - 1;
        while (start > 0)
        {
            var current = fixesOldestFirst[start];
            var previous = fixesOldestFirst[start - 1];
            if (IsImplausibleJump(previous, current, settings.MaxPlausibleSpeedKmh)) break;
            if (!IsGoodAndInside(previous, targetLatitude, targetLongitude, settings)) break;
            start--;
        }

        var insideSince = fixesOldestFirst[start].At;
        var dwelled = newest.At - insideSince;
        return dwelled >= settings.Dwell
            ? new GeofenceResult(GeofenceOutcome.Arrived, distance, insideSince, newest)
            : new GeofenceResult(GeofenceOutcome.DwellNotMet, distance, insideSince, newest);
    }

    public static bool IsGoodAndInside(GeoFix fix, double targetLatitude, double targetLongitude, GeofenceSettings settings) =>
        fix.AccuracyMeters is { } accuracy && double.IsFinite(accuracy) && accuracy <= settings.MaxAccuracyMeters &&
        DistanceMeters(fix.Latitude, fix.Longitude, targetLatitude, targetLongitude) <= settings.RadiusMeters;

    /// <summary>True when getting from <paramref name="previous"/> to <paramref name="current"/> would need an impossible speed.</summary>
    public static bool IsImplausibleJump(GeoFix previous, GeoFix current, double maxSpeedKmh)
    {
        var moved = DistanceMeters(previous.Latitude, previous.Longitude, current.Latitude, current.Longitude);
        if (!double.IsFinite(moved)) return true;
        if (moved <= JitterMeters) return false;
        var seconds = (current.At - previous.At).TotalSeconds;
        if (seconds <= 0) return true;                          // a different place at the same (or an earlier) instant
        return moved / seconds * 3.6 > maxSpeedKmh;
    }
}
