namespace GridPulse.Infrastructure.Messaging;

public sealed class MqttOptions
{
    public const string SectionName = "Mqtt";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1883;

    /// <summary>Client id prefix; a random suffix keeps replicas from kicking each other off the broker.</summary>
    public string ClientIdPrefix { get; set; } = "gridpulse";
}
