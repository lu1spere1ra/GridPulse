using GridPulse.Core.Alerts;
using GridPulse.Core.Contracts;
using GridPulse.Core.Readings;
using GridPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GridPulse.Ingestion.Alerts;

/// <summary>
/// Reloads alert rules and registered meters into the engine every 30 seconds, so rule edits made in
/// the dashboard take effect without a restart. On start-up it also restores alerts that were still
/// open, so they can resolve after an ingestion restart.
/// </summary>
public sealed class AlertRuleRefresher(
    AlertEngine engine,
    IMeterRegistry registry,
    IDbContextFactory<GridPulseDbContext> dbFactory,
    TimeProvider time,
    IOptions<IngestionOptions> options,
    ILogger<AlertRuleRefresher> logger) : BackgroundService
{
    private static readonly TimeSpan RuleRefreshInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RestoreOpenAlertsAsync(stoppingToken);

        using var timer = new PeriodicTimer(RuleRefreshInterval, time);
        do
        {
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
                var rules = await db.AlertRules.AsNoTracking().ToListAsync(stoppingToken);
                engine.UpdateRules(rules);
                engine.UpdateMeters(registry.MeterIds.ToArray());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Refreshing alert rules failed: {Message}", ex.Message);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RestoreOpenAlertsAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
                var open = await db.Alerts.AsNoTracking()
                    .Where(a => a.State == AlertState.Firing)
                    .Select(a => new { a.RuleId, a.MeterId })
                    .ToListAsync(cancellationToken);
                engine.RestoreOpen(open.Select(a => (a.RuleId, a.MeterId)));
                logger.LogInformation("Restored {Count} open alerts", open.Count);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < 10)
            {
                logger.LogWarning("Loading open alerts failed ({Message}); retrying", ex.Message);
                await Task.Delay(options.Value.TickInterval * 5, time, cancellationToken);
            }
        }
    }
}
