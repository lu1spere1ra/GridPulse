namespace GridPulse.Core.Readings;

/// <summary>Why a reading was not accepted. Exported as a metric tag, so keep names stable.</summary>
public enum RejectReason
{
    None = 0,
    Malformed,
    UnknownMeter,
    PowerOutOfRange,
    VoltageOutOfRange,
    TimestampInFuture,
    TimestampTooOld,
    DuplicateOrOutOfOrder,
    RegisterRegression,
}
