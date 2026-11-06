namespace GridPulse.Core.Contracts;

/// <summary>Idempotent bulk registration of meters by serial number.</summary>
public sealed record RegisterMetersRequest(IReadOnlyList<string> Serials);
