using GridPulse.Core.Alerts;
using GridPulse.Core.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GridPulse.Infrastructure.Persistence;

/// <summary>Seeds the default fleet-wide alert rules on an empty database.</summary>
public static class DefaultData
{
    public static async Task SeedAsync(GridPulseDbContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (await db.AlertRules.AnyAsync(cancellationToken))
        {
            return;
        }

        db.AlertRules.AddRange(
            new AlertRule
            {
                Name = "High consumption (1-min average above 7 kW)",
                Type = AlertRuleType.Threshold,
                ThresholdW = 7_000,
                Aggregate = ThresholdAggregate.Average,
            },
            new AlertRule
            {
                Name = "Meter offline for more than 5 minutes",
                Type = AlertRuleType.Offline,
                OfflineMinutes = 5,
            });
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded default alert rules");
    }
}
