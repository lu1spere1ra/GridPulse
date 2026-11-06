using GridPulse.Core.Alerts;
using GridPulse.Core.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GridPulse.Infrastructure.Persistence;

/// <summary>Applies alert transitions to the <c>alerts</c> table.</summary>
public sealed class AlertStore(IDbContextFactory<GridPulseDbContext> dbFactory)
{
    /// <summary>
    /// Returns the stored alert and its rule name, or <c>null</c> when nothing changed: a "firing"
    /// for an alert that is already open (the partial unique index rejects it), or a "resolved"
    /// for an alert that is not open.
    /// </summary>
    public async Task<(Alert Alert, string RuleName)?> ApplyAsync(AlertTransition transition, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        Alert? alert;

        if (transition.State == AlertState.Firing)
        {
            alert = new Alert
            {
                RuleId = transition.RuleId,
                MeterId = transition.MeterId,
                State = AlertState.Firing,
                RaisedAt = transition.At,
                Message = transition.Message,
            };
            db.Alerts.Add(alert);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return null;
            }
        }
        else
        {
            alert = await db.Alerts.FirstOrDefaultAsync(
                a => a.RuleId == transition.RuleId && a.MeterId == transition.MeterId && a.State == AlertState.Firing,
                cancellationToken);
            if (alert is null)
            {
                return null;
            }

            alert.State = AlertState.Resolved;
            alert.ResolvedAt = transition.At;
            alert.Message = $"{alert.Message} Resolved: {transition.Message}";
            await db.SaveChangesAsync(cancellationToken);
        }

        var ruleName = transition.RuleName.Length > 0
            ? transition.RuleName
            : await db.AlertRules.Where(r => r.Id == transition.RuleId).Select(r => r.Name).FirstOrDefaultAsync(cancellationToken) ?? "Deleted rule";
        return (alert, ruleName);
    }
}
