using System.Threading.Channels;
using GridPulse.Core.Aggregation;
using GridPulse.Core.Alerts;
using GridPulse.Core.Readings;
using GridPulse.Ingestion.Pipeline;

namespace GridPulse.Ingestion.Alerts;

/// <summary>
/// Connects the <see cref="AlertEngine"/> to the pipeline. Runs on the processor thread, so it only
/// evaluates and hands transitions over to <see cref="AlertDispatcher"/> through a channel: the
/// slow parts (database, MQTT, SMTP) never block ingestion.
/// </summary>
public sealed class AlertStreamListener(AlertEngine engine, ILogger<AlertStreamListener> logger) : IWindowListener
{
    // Bounded so an alert storm cannot grow memory without limit; alerts are dropped (and logged)
    // only if the dispatcher is thousands of transitions behind.
    private readonly Channel<AlertTransition> _transitions = Channel.CreateBounded<AlertTransition>(
        new BoundedChannelOptions(10_000) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropWrite });

    public ChannelReader<AlertTransition> Transitions => _transitions.Reader;

    public void OnReading(in Reading reading)
    {
        engine.OnReading(reading.MeterId, reading.Timestamp);
        Forward();
    }

    public void OnWindowClosed(in WindowResult window)
    {
        engine.OnWindowClosed(window);
        Forward();
    }

    public void OnTick(DateTimeOffset now)
    {
        engine.OnTick(now);
        Forward();
    }

    private void Forward()
    {
        var transitions = engine.DrainTransitions();
        foreach (var transition in transitions)
        {
            if (!_transitions.Writer.TryWrite(transition))
            {
                logger.LogWarning("Alert dispatcher is overloaded; dropped {State} for rule {Rule} on meter {Meter}", transition.State, transition.RuleId, transition.MeterId);
            }
        }
    }
}
