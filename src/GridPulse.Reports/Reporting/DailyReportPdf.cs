using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace GridPulse.Reports.Reporting;

/// <summary>
/// One-page-per-50-meters PDF summary: fleet totals, then the meters ordered by energy.
/// PDFsharp-MigraDoc (MIT) instead of QuestPDF, whose licence is commercial above a revenue
/// threshold (ADR-0008).
/// </summary>
public static class DailyReportPdf
{
    private static readonly Color Ink = Color.FromRgb(0x1C, 0x2B, 0x36);
    private static readonly Color Muted = Color.FromRgb(0x52, 0x64, 0x6F);
    private static readonly Color Rule = Color.FromRgb(0xD5, 0xDE, 0xE4);

    public static byte[] Render(DailyReport report)
    {
        EmbeddedFontResolver.EnsureInstalled();

        var document = new Document();
        document.Info.Title = $"GridPulse daily report {report.Date:yyyy-MM-dd}";
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "IBM Plex Sans";
        normal.Font.Size = 9;
        normal.Font.Color = Ink;

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromCentimeter(2);

        var title = section.AddParagraph($"Daily consumption, {report.Date:d MMMM yyyy}");
        title.Format.Font.Size = 16;
        title.Format.Font.Bold = true;
        var subtitle = section.AddParagraph($"UTC day, generated {report.GeneratedAt:yyyy-MM-dd HH:mm} UTC");
        subtitle.Format.Font.Color = Muted;
        subtitle.Format.SpaceAfter = Unit.FromPoint(14);

        var totals = section.AddTable();
        totals.AddColumn(Unit.FromCentimeter(4.25));
        totals.AddColumn(Unit.FromCentimeter(4.25));
        totals.AddColumn(Unit.FromCentimeter(4.25));
        totals.AddColumn(Unit.FromCentimeter(4.25));
        var labels = totals.AddRow();
        var values = totals.AddRow();
        AddStat(labels, values, 0, "Energy consumed", $"{N(report.TotalEnergyKwh, "N1")} kWh");
        AddStat(labels, values, 1, "Meters reporting", N(report.ReportingMeters, "N0"));
        AddStat(labels, values, 2, "Highest meter peak", $"{N(report.PeakMeterPowerW / 1000, "N2")} kW");
        AddStat(labels, values, 3, "Alerts raised", N(report.TotalAlerts, "N0"));

        var heading = section.AddParagraph("Meters by energy");
        heading.Format.Font.Bold = true;
        heading.Format.SpaceBefore = Unit.FromPoint(18);
        heading.Format.SpaceAfter = Unit.FromPoint(6);

        var table = section.AddTable();
        table.Borders.Bottom.Color = Rule;
        foreach (var width in new[] { 3.2, 4.8, 2.4, 2.4, 2.4, 1.8 })
        {
            table.AddColumn(Unit.FromCentimeter(width));
        }

        var header = table.AddRow();
        header.HeadingFormat = true;
        header.Format.Font.Bold = true;
        header.Borders.Bottom.Color = Ink;
        string[] titles = ["Meter", "Name", "Energy (kWh)", "Average (W)", "Peak (W)", "Alerts"];
        for (var i = 0; i < titles.Length; i++)
        {
            header.Cells[i].AddParagraph(titles[i]);
            header.Cells[i].Format.Alignment = i >= 2 ? ParagraphAlignment.Right : ParagraphAlignment.Left;
        }

        foreach (var meter in report.Meters.OrderByDescending(m => m.EnergyKwh))
        {
            var row = table.AddRow();
            row.Borders.Bottom.Color = Rule;
            row.Cells[0].AddParagraph(meter.Serial);
            row.Cells[1].AddParagraph(meter.Name ?? string.Empty);
            AddNumber(row, 2, N(meter.EnergyKwh, "N3"));
            AddNumber(row, 3, N(meter.AveragePowerW, "N0"));
            AddNumber(row, 4, N(meter.PeakPowerW, "N0"));
            AddNumber(row, 5, N(meter.Alerts, "N0"));
        }

        if (report.Meters.Count == 0)
        {
            section.AddParagraph("No meter reported on this day.").Format.Font.Color = Muted;
        }

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var output = new MemoryStream();
        renderer.PdfDocument.Save(output, closeStream: false);
        return output.ToArray();
    }

    private static void AddStat(Row labels, Row values, int column, string label, string value)
    {
        var l = labels.Cells[column].AddParagraph(label);
        l.Format.Font.Color = Muted;
        var v = values.Cells[column].AddParagraph(value);
        v.Format.Font.Size = 13;
        v.Format.Font.Bold = true;
    }

    private static void AddNumber(Row row, int column, string text)
    {
        row.Cells[column].AddParagraph(text);
        row.Cells[column].Format.Alignment = ParagraphAlignment.Right;
    }

    private static string N(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
}
