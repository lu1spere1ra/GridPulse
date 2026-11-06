using System.Globalization;

namespace GridPulse.Web.Services;

/// <summary>Display formatting shared by components (invariant culture, operator-friendly units).</summary>
public static class Format
{
    public static string Power(double watts) => Math.Abs(watts) switch
    {
        >= 1_000_000 => (watts / 1_000_000).ToString("0.00", CultureInfo.InvariantCulture) + " MW",
        >= 1_000 => (watts / 1_000).ToString("0.00", CultureInfo.InvariantCulture) + " kW",
        _ => watts.ToString("0", CultureInfo.InvariantCulture) + " W",
    };

    public static string Number(double value, string format = "N0") => value.ToString(format, CultureInfo.InvariantCulture);

    public static string Time(DateTimeOffset value) => value.ToUniversalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";

    public static string Ago(DateTimeOffset value, DateTimeOffset now)
    {
        var elapsed = now - value;
        return elapsed.TotalSeconds switch
        {
            < 60 => "just now",
            < 3_600 => $"{(int)elapsed.TotalMinutes} min ago",
            < 86_400 => $"{(int)elapsed.TotalHours} h ago",
            _ => value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        };
    }
}
