namespace GridPulse.Core.Contracts;

/// <summary>MQTT topic names shared by every service.</summary>
public static class MqttTopics
{
    public const string ReadingsSubscription = "gridpulse/meters/+/readings";
    public const string LiveSubscription = "gridpulse/live/#";
    public const string LiveFleet = "gridpulse/live/fleet";
    public const string LiveMeters = "gridpulse/live/meters";
    public const string LiveWindows = "gridpulse/live/windows";
    public const string LiveAlerts = "gridpulse/live/alerts";

    public static string Readings(string meterSerial) => $"gridpulse/meters/{meterSerial}/readings";
}
