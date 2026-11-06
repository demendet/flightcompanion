namespace FlightCompanion.Game.Aviation;

public static class Units
{
    public const double LbPerKg = 2.20462262;
    public const double MetersPerNm = 1852.0;
    public const double FeetPerMeter = 3.28084;
    public static double KgToLb(double kg) => kg * LbPerKg;
    public static double LbToKg(double lb) => lb / LbPerKg;
}

public static class Geo
{
    private const double EarthRadiusM = 6371000.0;

    public static double DistanceM(double lat1, double lon1, double lat2, double lon2)
    {
        double dLat = Rad(lat2 - lat1), dLon = Rad(lon2 - lon1);
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusM * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
    }

    public static double DistanceNm(double lat1, double lon1, double lat2, double lon2)
        => DistanceM(lat1, lon1, lat2, lon2) / Units.MetersPerNm;

    public static double BearingDeg(double lat1, double lon1, double lat2, double lon2)
    {
        double y = Math.Sin(Rad(lon2 - lon1)) * Math.Cos(Rad(lat2));
        double x = Math.Cos(Rad(lat1)) * Math.Sin(Rad(lat2))
                 - Math.Sin(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Cos(Rad(lon2 - lon1));
        return (Math.Atan2(y, x) * 180.0 / Math.PI + 360.0) % 360.0;
    }

    /// <summary>The point a given distance and bearing away. Used to place people beside a parking spot.</summary>
    public static (double lat, double lon) Offset(double lat, double lon, double bearingDeg, double meters)
    {
        double d = meters / EarthRadiusM, b = Rad(bearingDeg), la1 = Rad(lat), lo1 = Rad(lon);
        double la2 = Math.Asin(Math.Sin(la1) * Math.Cos(d) + Math.Cos(la1) * Math.Sin(d) * Math.Cos(b));
        double lo2 = lo1 + Math.Atan2(Math.Sin(b) * Math.Sin(d) * Math.Cos(la1), Math.Cos(d) - Math.Sin(la1) * Math.Sin(la2));
        return (la2 * 180.0 / Math.PI, lo2 * 180.0 / Math.PI);
    }

    /// <summary>Headwind component in knots for a course; negative means a tailwind.</summary>
    public static double HeadwindKt(double courseDeg, double windFromDeg, double windKt)
        => windKt * Math.Cos(Rad(windFromDeg - courseDeg));

    private static double Rad(double deg) => deg * Math.PI / 180.0;
}

/// <summary>Sun position, good enough to know whether a pickup is in the dark. At 69.5 N the polar night is real gameplay.</summary>
public static class Sun
{
    public static double ElevationDeg(DateTimeOffset when, double lat, double lon)
    {
        var utc = when.UtcDateTime;
        double dayOfYear = utc.DayOfYear + (utc.Hour + utc.Minute / 60.0) / 24.0;
        double g = 2 * Math.PI / 365.0 * (dayOfYear - 1);
        double decl = 0.006918 - 0.399912 * Math.Cos(g) + 0.070257 * Math.Sin(g) - 0.006758 * Math.Cos(2 * g)
                    + 0.000907 * Math.Sin(2 * g) - 0.002697 * Math.Cos(3 * g) + 0.00148 * Math.Sin(3 * g);
        double eq = 229.18 * (0.000075 + 0.001868 * Math.Cos(g) - 0.032077 * Math.Sin(g)
                    - 0.014615 * Math.Cos(2 * g) - 0.040849 * Math.Sin(2 * g));
        double minutes = utc.Hour * 60 + utc.Minute + utc.Second / 60.0 + eq + 4 * lon;
        double hourAngle = (minutes / 4.0 - 180.0) * Math.PI / 180.0;
        double latRad = lat * Math.PI / 180.0;
        double sinEl = Math.Sin(latRad) * Math.Sin(decl) + Math.Cos(latRad) * Math.Cos(decl) * Math.Cos(hourAngle);
        return Math.Asin(Math.Clamp(sinEl, -1, 1)) * 180.0 / Math.PI;
    }

    /// <summary>Civil twilight or brighter counts as usable daylight.</summary>
    public static bool IsDark(DateTimeOffset when, double lat, double lon) => ElevationDeg(when, lat, lon) < -6.0;
}
