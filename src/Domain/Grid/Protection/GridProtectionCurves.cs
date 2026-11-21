using Domain.Grid.Definition;

namespace Domain.Grid.Protection;

public static class GridProtectionCurves
{
    public static double OperateSeconds(GridCurveType curve, double multiple, double timeSetting)
    {
        if (multiple <= 1 || !double.IsFinite(multiple)) return double.PositiveInfinity;
        return curve switch
        {
            GridCurveType.IecStandardInverse => timeSetting * 0.14 / (Math.Pow(multiple, 0.02) - 1),
            GridCurveType.IecVeryInverse => timeSetting * 13.5 / (multiple - 1),
            GridCurveType.IecExtremelyInverse => timeSetting * 80 / (multiple * multiple - 1),
            _ => timeSetting
        };
    }
}
