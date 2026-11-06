using GridPulse.Core.Contracts;
using GridPulse.Core.Readings;
using GridPulse.Infrastructure.Persistence;
using GridPulse.Ingestion.Mqtt;
using GridPulse.Ingestion.Pipeline;

namespace GridPulse.Ingestion.Alerts;

/// <summary>
/// Turns engine transitions into stored alerts (EF Core), live notifications (MQTT → SignalR) and emails.
/// A single reader, so the transitions of one alert are applied in order.
/// </summary>
public sealed class AlertDispatcher(
    AlertStreamListener listener,
    AlertStore store,
    IMeterRegistry registry,
    MqttLivePublisher live,
    IAlertNotifier notifier,
    IngestionMetrics metrics,
    ILogger<AlertDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var transition in listener.Transitions.ReadAllAsync(stoppingToken))
        {
            try
            {
                if (await store.ApplyAsync(transition, stoppingToken) is not var (alert, ruleName))
                {
                    continue;
                }

                if (alert.State == AlertState.Firing)
                {
                    metrics.AlertRaised(transition.RuleType);
                }

                var notification = new AlertNotification(
                    alert.Id, alert.RuleId, ruleName, transition.RuleType, alert.MeterId,
                    registry.GetSerial(alert.MeterId) ?? $"#{alert.MeterId}",
                    alert.State, alert.RaisedAt, alert.ResolvedAt, alert.Message);

                await live.PublishAlertAsync(notification, stoppingToken);
                await notifier.NotifyAsync(notification, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Dispatching {State} for rule {Rule} on meter {Meter} failed", transition.State, transition.RuleId, transition.MeterId);
            }
        }
    }
}
