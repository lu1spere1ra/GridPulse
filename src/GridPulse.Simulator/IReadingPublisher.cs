using GridPulse.Core.Contracts;

namespace GridPulse.Simulator;

public interface IReadingPublisher
{
    Task ConnectAsync(CancellationToken cancellationToken);

    Task PublishAsync(ReadingMessage reading, CancellationToken cancellationToken);
}
