using GridPulse.Core.Contracts;

namespace GridPulse.Core.Readings;

/// <summary>
/// Stateless checks that only need the reading itself and the current time.
/// Stateful checks (ordering, register regression) live in <see cref="MeterSequenceGuard"/>.
/// </summary>
public sealed class ReadingValidator(ReadingValidationOptions options)
{
    public RejectReason Validate(ReadingMessage message, DateTimeOffset now)
    {
        if (!double.IsFinite(message.PowerW) || message.PowerW < 0 || message.PowerW > options.MaxPowerW)
        {
            return RejectReason.PowerOutOfRange;
        }

        if (!double.IsFinite(message.VoltageV) || message.VoltageV < options.MinVoltageV || message.VoltageV > options.MaxVoltageV)
        {
            return RejectReason.VoltageOutOfRange;
        }

        if (message.Ts > now + options.MaxClockSkew)
        {
            return RejectReason.TimestampInFuture;
        }

        if (message.Ts < now - options.MaxAge)
        {
            return RejectReason.TimestampTooOld;
        }

        return message.EnergyWh < 0 ? RejectReason.Malformed : RejectReason.None;
    }
}
