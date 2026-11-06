namespace GridPulse.Simulator;

/// <summary>
/// A typical residential daily curve: low at night, a morning peak around 07:30 and a
/// larger evening peak around 19:30. Returns a factor in [0, 1].
/// </summary>
public static class LoadProfile
{
    public static double DailyFactor(double hourOfDay)
    {
        var morning = Bump(hourOfDay, centre: 7.5, width: 1.2) * 0.6;
        var midday = Bump(hourOfDay, centre: 13.0, width: 2.0) * 0.3;
        var evening = Bump(hourOfDay, centre: 19.5, width: 2.0);
        return Math.Clamp(morning + midday + evening, 0, 1);
    }

    private static double Bump(double hour, double centre, double width)
    {
        // Distance on a 24 h circle so the evening peak wraps smoothly past midnight.
        var distance = Math.Abs(hour - centre);
        distance = Math.Min(distance, 24 - distance);
        return Math.Exp(-(distance * distance) / (2 * width * width));
    }
}
