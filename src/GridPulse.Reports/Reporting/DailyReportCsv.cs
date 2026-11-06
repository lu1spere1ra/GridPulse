using System.Globalization;
using System.Text;

namespace GridPulse.Reports.Reporting;

/// <summary>
/// RFC 4180 CSV, invariant culture (a decimal point, never a comma), UTF-8 without BOM.
/// Hand-written: one table with fixed columns does not need a CSV library.
/// </summary>
public static class DailyReportCsv
{
    private static readonly string[] Header =
        ["date", "meter_id", "serial", "name", "energy_kwh", "average_power_w", "peak_power_w", "samples", "alerts"];

    public static byte[] Write(DailyReport report)
    {
        var csv = new StringBuilder();
        csv.AppendJoin(',', Header).Append("\r\n");
        foreach (var meter in report.Meters.OrderBy(m => m.Serial, StringComparer.Ordinal))
        {
            csv.AppendJoin(',',
                    report.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    meter.MeterId.ToString(CultureInfo.InvariantCulture),
                    Escape(meter.Serial),
                    Escape(meter.Name ?? string.Empty),
                    meter.EnergyKwh.ToString("0.000", CultureInfo.InvariantCulture),
                    meter.AveragePowerW.ToString("0.0", CultureInfo.InvariantCulture),
                    meter.PeakPowerW.ToString("0.0", CultureInfo.InvariantCulture),
                    meter.Samples.ToString(CultureInfo.InvariantCulture),
                    meter.Alerts.ToString(CultureInfo.InvariantCulture))
                .Append("\r\n");
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(csv.ToString());
    }

    /// <summary>Quotes a field that contains a comma, quote or line break, doubling inner quotes.</summary>
    internal static string Escape(string value) =>
        value.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
}
