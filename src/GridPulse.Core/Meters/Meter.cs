namespace GridPulse.Core.Meters;

/// <summary>A registered smart meter.</summary>
public sealed class Meter
{
    public int Id { get; set; }

    /// <summary>Serial number as printed on the device and used in MQTT topics, e.g. <c>MTR-000123</c>.</summary>
    public required string Serial { get; set; }

    public string? Name { get; set; }

    /// <summary>Free-form grouping, e.g. a neighbourhood or a substation.</summary>
    public string? Zone { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
