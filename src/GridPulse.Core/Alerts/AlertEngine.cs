using GridPulse.Core.Aggregation;
using GridPulse.Core.Contracts;

namespace GridPulse.Core.Alerts;

/// <summary>A rule started or stopped firing for a meter.</summary>
public readonly record struct AlertTransition(
    int RuleId,
    string RuleName,
    AlertRuleType RuleType,
    int MeterId,
    AlertState State,
    DateTimeOffset At,
    string Message);

/// <summary>
/// Evaluates alert rules against the stream. Pure logic: no I/O and no clock of its own (the
/// caller passes the time), so every timing case is a plain unit test.
/// </summary>
/// <remarks>
/// <para>Threshold rules are checked on every closed 1-minute window. Offline rules are checked on
/// every tick against the last time each meter reported.</para>
/// <para>
/// State machine per (rule, meter): <c>Ok → Firing → Resolved/Ok</c>, with at most one open alert.
/// A threshold alert resolves only when the value drops below <see cref="ResolveRatio"/> of the
/// limit, so a meter hovering around the limit does not flap.
/// </para>
/// <para>
/// Threading: <see cref="OnReading"/>, <see cref="OnWindowClosed"/> and <see cref="OnTick"/> run on
/// the pipeline's processor thread. <see cref="UpdateRules"/> and <see cref="UpdateMeters"/> may be
/// called from any thread; the new values are published atomically and picked up on the next tick.
/// </para>
/// </remarks>
public sealed class AlertEngine
{
    public const double ResolveRatio = 0.9;

    private readonly Dictionary<int, DateTimeOffset> _lastSeen = [];
    private readonly HashSet<(int RuleId, int MeterId)> _open = [];
    private readonly List<AlertTransition> _transitions = [];
    private RuleSet _rules = RuleSet.Empty;
    private volatile RuleSet? _pendingRules;
    private volatile IReadOnlyCollection<int>? _pendingMeters;

    /// <summary>Replaces the rule set (picked up on the next tick).</summary>
    public void UpdateRules(IEnumerable<AlertRule> rules) => _pendingRules = new RuleSet(rules.Where(r => r.Enabled).ToArray());

    /// <summary>Registered meters, so meters that never report also go offline (picked up on the next tick).</summary>
    public void UpdateMeters(IReadOnlyCollection<int> meterIds) => _pendingMeters = meterIds;

    /// <summary>Alerts still open in storage from before a restart, so they can be resolved later.</summary>
    public void RestoreOpen(IEnumerable<(int RuleId, int MeterId)> open)
    {
        foreach (var key in open)
        {
            _open.Add(key);
        }
    }

    public void OnReading(int meterId, DateTimeOffset timestamp)
    {
        _lastSeen[meterId] = timestamp;
        foreach (var rule in _rules.For(AlertRuleType.Offline, meterId))
        {
            if (_open.Remove((rule.Id, meterId)))
            {
                Emit(rule, meterId, AlertState.Resolved, timestamp, "Meter is reporting again.");
            }
        }
    }

    public void OnWindowClosed(in WindowResult window)
    {
        if (window.Size != TimeSpan.FromMinutes(1))
        {
            return;
        }

        foreach (var rule in _rules.For(AlertRuleType.Threshold, window.MeterId))
        {
            var limit = rule.ThresholdW!.Value;
            var value = rule.Aggregate == ThresholdAggregate.Average ? window.AvgPowerW : window.MaxPowerW;
            var key = (rule.Id, window.MeterId);
            var statistic = rule.Aggregate == ThresholdAggregate.Average ? "average" : "peak";

            if (value > limit && _open.Add(key))
            {
                Emit(rule, window.MeterId, AlertState.Firing, window.End,
                    $"1-minute {statistic} power {Kw(value)} is above the {Kw(limit)} limit.");
            }
            else if (value < limit * ResolveRatio && _open.Remove(key))
            {
                Emit(rule, window.MeterId, AlertState.Resolved, window.End,
                    $"1-minute {statistic} power is back to {Kw(value)}.");
            }
        }
    }

    public void OnTick(DateTimeOffset now)
    {
        ApplyPendingUpdates(now);

        foreach (var (meterId, lastSeen) in _lastSeen)
        {
            foreach (var rule in _rules.For(AlertRuleType.Offline, meterId))
            {
                var silence = now - lastSeen;
                if (silence > TimeSpan.FromMinutes(rule.OfflineMinutes!.Value) && _open.Add((rule.Id, meterId)))
                {
                    Emit(rule, meterId, AlertState.Firing, now,
                        $"No reading for {(int)silence.TotalMinutes} minutes (limit {rule.OfflineMinutes} min).");
                }
            }
        }
    }

    /// <summary>Returns and clears the transitions produced since the last call.</summary>
    public IReadOnlyList<AlertTransition> DrainTransitions()
    {
        if (_transitions.Count == 0)
        {
            return [];
        }

        var drained = _transitions.ToArray();
        _transitions.Clear();
        return drained;
    }

    private void ApplyPendingUpdates(DateTimeOffset now)
    {
        if (Interlocked.Exchange(ref _pendingMeters, null) is { } meters)
        {
            foreach (var meterId in meters)
            {
                // A newly known meter gets a grace period starting now, not an instant offline alert.
                _lastSeen.TryAdd(meterId, now);
            }
        }

        if (Interlocked.Exchange(ref _pendingRules, null) is { } rules)
        {
            _rules = rules;

            // Alerts of rules that were deleted or disabled can never resolve on their own.
            foreach (var key in _open.Where(k => !rules.Contains(k.RuleId)).ToArray())
            {
                _open.Remove(key);
                _transitions.Add(new AlertTransition(key.RuleId, string.Empty, AlertRuleType.Threshold, key.MeterId, AlertState.Resolved, now, "Rule disabled or deleted."));
            }
        }
    }

    private void Emit(AlertRule rule, int meterId, AlertState state, DateTimeOffset at, string message) =>
        _transitions.Add(new AlertTransition(rule.Id, rule.Name, rule.Type, meterId, state, at, message));

    private static string Kw(double watts) => $"{watts / 1000:0.00} kW";

    /// <summary>Immutable rule set with per-meter precedence: a meter-specific rule replaces fleet-wide rules of the same type.</summary>
    private sealed class RuleSet(AlertRule[] rules)
    {
        public static RuleSet Empty { get; } = new([]);

        private readonly Dictionary<(AlertRuleType, int), AlertRule[]> _specific = rules
            .Where(r => r.MeterId is not null)
            .GroupBy(r => (r.Type, r.MeterId!.Value))
            .ToDictionary(g => g.Key, g => g.ToArray());

        private readonly Dictionary<AlertRuleType, AlertRule[]> _fleet = rules
            .Where(r => r.MeterId is null)
            .GroupBy(r => r.Type)
            .ToDictionary(g => g.Key, g => g.ToArray());

        private readonly HashSet<int> _ids = rules.Select(r => r.Id).ToHashSet();

        public bool Contains(int ruleId) => _ids.Contains(ruleId);

        public AlertRule[] For(AlertRuleType type, int meterId) =>
            _specific.TryGetValue((type, meterId), out var specific) ? specific
            : _fleet.TryGetValue(type, out var fleet) ? fleet
            : [];
    }
}
