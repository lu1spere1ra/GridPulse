using System.Globalization;
using GridPulse.Reports.Reporting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace GridPulse.Reports;

public sealed class DailyReportFunctions(DailyReportService reports, TimeProvider time)
{
    /// <summary>Every day at 00:15 UTC: the previous UTC day, once its last hourly bucket has settled.</summary>
    [Function("DailyReport")]
    public Task RunOnScheduleAsync([TimerTrigger("0 15 0 * * *")] TimerInfo timer, CancellationToken cancellationToken) =>
        reports.GenerateAsync(Yesterday(), cancellationToken);

    /// <summary>
    /// Manual run, e.g. for a demo or to regenerate a day: <c>POST /api/reports/daily?date=2026-11-05</c>.
    /// Protected by a function key in Azure.
    /// </summary>
    [Function("DailyReportOnDemand")]
    public async Task<IActionResult> RunOnDemandAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "reports/daily")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var date = Yesterday();
        if (request.Query.TryGetValue("date", out var value)
            && !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return new BadRequestObjectResult(new { error = "Use ?date=yyyy-MM-dd." });
        }

        return new OkObjectResult(await reports.GenerateAsync(date, cancellationToken));
    }

    private DateOnly Yesterday() => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).AddDays(-1);
}
