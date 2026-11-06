using System.Text;
using GridPulse.Reports.Reporting;

namespace GridPulse.UnitTests.Reports;

public sealed class DailyReportTests
{
    private static readonly DailyReport Report = new(
        new DateOnly(2026, 11, 5),
        new DateTimeOffset(2026, 11, 6, 0, 15, 0, TimeSpan.Zero),
        [
            new MeterDay(2, "MTR-000002", "Bakery, \"Main\" street", 31.2345, 1_301.25, 9_870.5, 86_400, 2),
            new MeterDay(1, "MTR-000001", null, 12.5, 520.9, 2_100, 86_390, 0),
        ]);

    [Fact]
    public void Csv_has_a_header_one_row_per_meter_and_invariant_numbers()
    {
        var lines = Encoding.UTF8.GetString(DailyReportCsv.Write(Report)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        lines[0].ShouldBe("date,meter_id,serial,name,energy_kwh,average_power_w,peak_power_w,samples,alerts");
        lines[1].ShouldBe("2026-11-05,1,MTR-000001,,12.500,520.9,2100.0,86390,0");
        lines[2].ShouldBe("2026-11-05,2,MTR-000002,\"Bakery, \"\"Main\"\" street\",31.235,1301.3,9870.5,86400,2");
    }

    [Fact]
    public void Csv_has_no_byte_order_mark()
    {
        DailyReportCsv.Write(Report)[0].ShouldBe((byte)'d');
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("line\nbreak", "\"line\nbreak\"")]
    public void Csv_escapes_only_fields_that_need_it(string value, string expected) =>
        DailyReportCsv.Escape(value).ShouldBe(expected);

    [Fact]
    public void Pdf_renders_a_valid_document()
    {
        var pdf = DailyReportPdf.Render(Report);

        Encoding.ASCII.GetString(pdf, 0, 5).ShouldBe("%PDF-");
        pdf.Length.ShouldBeGreaterThan(5_000, "the embedded font alone is larger than this");
    }

    [Fact]
    public void Pdf_renders_an_empty_day()
    {
        var pdf = DailyReportPdf.Render(Report with { Meters = [] });

        Encoding.ASCII.GetString(pdf, 0, 5).ShouldBe("%PDF-");
    }

    [Fact]
    public void Totals_and_blob_names_are_derived_from_the_day()
    {
        Report.TotalEnergyKwh.ShouldBe(43.7345, tolerance: 1e-9);
        Report.TotalAlerts.ShouldBe(2);
        Report.BlobName("pdf").ShouldBe("daily/2026/11/05/gridpulse-daily-2026-11-05.pdf");
    }
}
