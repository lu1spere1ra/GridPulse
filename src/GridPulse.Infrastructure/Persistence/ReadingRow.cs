namespace GridPulse.Infrastructure.Persistence;

/// <summary>
/// Raw reading as stored in the <c>readings</c> hypertable. Mapped in EF Core only so that
/// migrations own the table; rows are written with binary COPY and read with Dapper.
/// </summary>
public sealed class ReadingRow
{
    public DateTimeOffset Time { get; set; }

    public int MeterId { get; set; }

    public float PowerW { get; set; }

    public long EnergyWh { get; set; }

    public float VoltageV { get; set; }
}
