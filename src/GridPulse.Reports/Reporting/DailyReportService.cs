using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;

namespace GridPulse.Reports.Reporting;

public sealed record ReportLocation(string Csv, string Pdf);

/// <summary>Builds the daily report and stores CSV + PDF in Blob Storage. Re-running a day overwrites it (idempotent).</summary>
public sealed class DailyReportService(
    DailyReportQuery query,
    BlobServiceClient blobs,
    TimeProvider time,
    ILogger<DailyReportService> logger)
{
    public const string Container = "reports";

    public async Task<ReportLocation> GenerateAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var report = new DailyReport(date, time.GetUtcNow(), await query.GetAsync(date, cancellationToken));

        var container = blobs.GetBlobContainerClient(Container);
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var csv = await UploadAsync(container, report.BlobName("csv"), DailyReportCsv.Write(report), "text/csv; charset=utf-8", cancellationToken);
        var pdf = await UploadAsync(container, report.BlobName("pdf"), DailyReportPdf.Render(report), "application/pdf", cancellationToken);

        logger.LogInformation(
            "Daily report for {Date}: {Meters} meters, {Energy:N1} kWh, {Alerts} alerts",
            date, report.Meters.Count, report.TotalEnergyKwh, report.TotalAlerts);
        return new ReportLocation(csv, pdf);
    }

    private static async Task<string> UploadAsync(BlobContainerClient container, string name, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        var blob = container.GetBlobClient(name);
        await blob.UploadAsync(
            new BinaryData(content),
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            cancellationToken);
        return blob.Uri.ToString();
    }
}
