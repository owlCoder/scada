using Domain.Grid.Definition;

namespace Domain.Grid.Profiles;

public static class GridProfiles
{
    public static double Load(GridLoadProfile profile, TimeSpan time) =>
        (profile == GridLoadProfile.Constant ? 1 : WeekendFactor(profile, time)) * Shape(profile, time);

    private static double WeekendFactor(GridLoadProfile profile, TimeSpan time) =>
        (int)Math.Floor(time.TotalDays) % 7 is 5 or 6 ? profile switch
        {
            GridLoadProfile.Commercial => 0.65,
            GridLoadProfile.Industrial => 0.8,
            _ => 1.05
        } : 1;

    private static double Shape(GridLoadProfile profile, TimeSpan time) => profile switch
    {
        GridLoadProfile.Residential => Interpolate(time, [0.35, 0.28, 0.27, 0.26, 0.28, 0.38, 0.55, 0.65, 0.58, 0.48, 0.44, 0.45,
            0.5, 0.48, 0.46, 0.5, 0.62, 0.8, 0.95, 1, 0.92, 0.78, 0.6, 0.44]),
        GridLoadProfile.Commercial => Interpolate(time, [0.2, 0.18, 0.17, 0.17, 0.18, 0.22, 0.35, 0.6, 0.85, 0.95, 1, 1,
            0.9, 0.95, 1, 0.98, 0.9, 0.75, 0.5, 0.35, 0.28, 0.25, 0.23, 0.21]),
        GridLoadProfile.Industrial => Interpolate(time, [0.55, 0.55, 0.55, 0.55, 0.55, 0.65, 0.85, 0.95, 1, 1, 1, 1,
            0.9, 0.95, 1, 1, 0.95, 0.9, 0.85, 0.8, 0.75, 0.65, 0.6, 0.55]),
        _ => 1
    };

    // Simulation day zero is an illustrative spring weekday, not a dated annual production forecast.
    public static double Sun(TimeSpan time)
    {
        var day = Math.Floor(time.TotalDays);
        var daylight = 12 + 3 * Math.Sin(2 * Math.PI * day / 365);
        var hour = (time.TotalHours % 24 + 24) % 24;
        var angle = Math.PI * (hour - (12 - daylight / 2)) / daylight;
        var weather = 0.72 + 0.18 * Math.Sin(day * 1.73 + 0.4);
        return angle is > 0 and < Math.PI ? weather * Math.Pow(Math.Sin(angle), 1.5) : 0;
    }

    public static double DemandVariation(TimeSpan time) => 1 + 0.015 * Math.Sin(time.TotalHours * Math.PI / 3);

    public static double VoltageFactor(double voltagePu) => 0.4 + 0.3 * voltagePu + 0.3 * voltagePu * voltagePu;

    private static double Interpolate(TimeSpan time, double[] values)
    {
        var hour = (time.TotalHours % 24 + 24) % 24;
        var index = (int)hour;
        return values[index] + (values[(index + 1) % 24] - values[index]) * (hour - index);
    }
}
