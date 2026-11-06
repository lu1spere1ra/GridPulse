using GridPulse.Core.Contracts;

namespace GridPulse.Ingestion.Alerts;

public interface IAlertNotifier
{
    Task NotifyAsync(AlertNotification alert, CancellationToken cancellationToken);
}
