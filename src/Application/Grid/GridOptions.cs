using System.Globalization;

namespace Application.Grid;

public sealed class GridOptions
{
    public bool Enabled { get; set; } = true;
    public double StepSeconds { get; set; } = 1;
    public double TimeScale { get; set; } = 60;
    public int RandomSeed { get; set; } = 104;
    public double SelectTimeoutSeconds { get; set; } = 30;
    public string StartTimeOfDay { get; set; } = "08:00";

    public TimeSpan StartTime => TimeSpan.ParseExact(StartTimeOfDay, @"hh\:mm", CultureInfo.InvariantCulture);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!double.IsFinite(StepSeconds) || StepSeconds is <= 0 or > 60) errors.Add("Grid:StepSeconds must be in (0,60].");
        if (!double.IsFinite(TimeScale) || TimeScale is <= 0 or > 1000 || StepSeconds / TimeScale < 0.001)
            errors.Add("Grid:TimeScale must be in (0,1000] with at least 1 ms between steps.");
        if (!double.IsFinite(SelectTimeoutSeconds) || SelectTimeoutSeconds is <= 0 or > 3600) errors.Add("Grid:SelectTimeoutSeconds must be in (0,3600].");
        if (!TimeSpan.TryParseExact(StartTimeOfDay, @"hh\:mm", CultureInfo.InvariantCulture, out var start) || start.TotalHours >= 24)
            errors.Add("Grid:StartTimeOfDay must be HH:mm in a 24-hour day.");
        return errors;
    }
}
