using Domain.Grid.Definition;

namespace Domain.Grid.Profiles;

public static class GridProfiles
{
    public static double Load(GridLoadProfile profile, TimeSpan time) => profile switch
    {
        GridLoadProfile.Residential => Interpolate(time, [0.35, 0.28, 0.27, 0.26, 0.28, 0.38, 0.55, 0.65, 0.58, 0.48, 0.44, 0.45,
            0.5, 0.48, 0.46, 0.5, 0.62, 0.8, 0.95, 1, 0.92, 0.78, 0.6, 0.44]),
        GridLoadProfile.Commercial => Interpolate(time, [0.2, 0.18, 0.17, 0.17, 0.18, 0.22, 0.35, 0.6, 0.85, 0.95, 1, 1,
            0.9, 0.95, 1, 0.98, 0.9, 0.75, 0.5, 0.35, 0.28, 0.25, 0.23, 0.21]),
        GridLoadProfile.Industrial => Interpolate(time, [0.55, 0.55, 0.55, 0.55, 0.55, 0.65, 0.85, 0.95, 1, 1, 1, 1,
            0.9, 0.95, 1, 1, 0.95, 0.9, 0.85, 0.8, 0.75, 0.65, 0.6, 0.55]),
        _ => 1
    };

    public static double Sun(TimeSpan time) => Math.Max(0, Math.Sin(Math.PI * ((time.TotalHours % 24) - 6) / 12));
    public static double VoltageFactor(double voltagePu) => 0.4 + 0.3 * voltagePu + 0.3 * voltagePu * voltagePu;

    private static double Interpolate(TimeSpan time, double[] values)
    {
        var hour = (time.TotalHours % 24 + 24) % 24;
        var index = (int)hour;
        return values[index] + (values[(index + 1) % 24] - values[index]) * (hour - index);
    }
}
