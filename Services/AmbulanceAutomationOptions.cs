namespace HospitalManagement.Api.Services;

/// <summary>
/// Settings for GPS-assisted arrival detection (configuration section "AmbulanceAutomation"). Nothing here is hard-coded in the
/// workflow: every threshold below can be changed in appsettings or with environment variables (AmbulanceAutomation__DwellSeconds).
/// Invalid values (zero, negative, NaN) silently fall back to the defaults, so a typo can never switch the safety checks off.
/// </summary>
public class AmbulanceAutomationOptions
{
    public const string SectionName = "AmbulanceAutomation";

    public const double DefaultPickupRadiusMeters = 100;
    public const double DefaultHospitalRadiusMeters = 150;
    public const double DefaultMaxAccuracyMeters = 50;
    public const int DefaultDwellSeconds = 15;
    public const double DefaultMaxPlausibleSpeedKmh = 200;

    /// <summary>Master switch. When false the driver confirms every step by hand (the previous behaviour).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The ambulance must be this close to the patient pickup point to count as arrived.</summary>
    public double PickupRadiusMeters { get; set; } = DefaultPickupRadiusMeters;

    /// <summary>The ambulance must be this close to the hospital to count as arrived.</summary>
    public double HospitalRadiusMeters { get; set; } = DefaultHospitalRadiusMeters;

    /// <summary>A GPS fix whose reported accuracy is worse than this is ignored for arrival detection.</summary>
    public double MaxAccuracyMeters { get; set; } = DefaultMaxAccuracyMeters;

    /// <summary>The ambulance must stay inside the radius, with good fixes, for this long before arrival is recorded.</summary>
    public int DwellSeconds { get; set; } = DefaultDwellSeconds;

    /// <summary>A jump between two fixes that implies a higher speed than this is treated as a GPS glitch.</summary>
    public double MaxPlausibleSpeedKmh { get; set; } = DefaultMaxPlausibleSpeedKmh;

    /// <summary>
    /// The destination hospital. The HMS serves one hospital and an ambulance request stores no destination, so it is configured here.
    /// Without coordinates hospital arrival is never detected automatically; the driver uses the manual fallback.
    /// </summary>
    public string? HospitalName { get; set; }
    public double? HospitalLatitude { get; set; }
    public double? HospitalLongitude { get; set; }

    public bool HasHospitalLocation =>
        HospitalLatitude is >= -90 and <= 90 && HospitalLongitude is >= -180 and <= 180;

    public string? HospitalDisplayName => string.IsNullOrWhiteSpace(HospitalName) ? null : HospitalName.Trim();

    public GeofenceSettings PickupSettings() => BuildSettings(PickupRadiusMeters, DefaultPickupRadiusMeters);

    public GeofenceSettings HospitalSettings() => BuildSettings(HospitalRadiusMeters, DefaultHospitalRadiusMeters);

    /// <summary>The effective accuracy limit (the configured value, or the default if the configured one is unusable).</summary>
    public double EffectiveMaxAccuracyMeters => Positive(MaxAccuracyMeters, DefaultMaxAccuracyMeters);

    private GeofenceSettings BuildSettings(double radius, double fallbackRadius) => new(
        Positive(radius, fallbackRadius),
        EffectiveMaxAccuracyMeters,
        TimeSpan.FromSeconds(DwellSeconds >= 0 ? DwellSeconds : DefaultDwellSeconds),
        Positive(MaxPlausibleSpeedKmh, DefaultMaxPlausibleSpeedKmh));

    private static double Positive(double value, double fallback) =>
        double.IsFinite(value) && value > 0 ? value : fallback;
}
