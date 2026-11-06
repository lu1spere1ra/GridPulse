using GridPulse.Core.Contracts;

namespace GridPulse.Core.Alerts;

/// <summary>One occurrence of a rule firing for one meter, from raised to resolved.</summary>
public sealed class Alert
{
    public long Id { get; set; }

    public int RuleId { get; set; }

    public int MeterId { get; set; }

    public AlertState State { get; set; }

    public DateTimeOffset RaisedAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public DateTimeOffset? AcknowledgedAt { get; set; }

    public required string Message { get; set; }
}
