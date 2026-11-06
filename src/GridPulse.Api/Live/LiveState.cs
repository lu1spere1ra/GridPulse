using GridPulse.Core.Contracts;

namespace GridPulse.Api.Live;

/// <summary>Last fleet snapshot received, so a dashboard that just connected has something to show.</summary>
public sealed class LiveState
{
    private volatile FleetSnapshot? _fleet;

    public FleetSnapshot? Fleet
    {
        get => _fleet;
        set => _fleet = value;
    }
}
