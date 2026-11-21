namespace Application.Simulation;

public sealed class SimulationOptions
{
    /// <summary>Simulated seconds advanced per step.</summary>
    public double StepSeconds { get; set; } = 0.5;

    /// <summary>Simulated seconds per wall-clock second while running.</summary>
    public double TimeScale { get; set; } = 5;

    /// <summary>Seed for deterministic network impairments.</summary>
    public int RandomSeed { get; set; } = 1337;

    /// <summary>Historian sampling interval in steps.</summary>
    public int TelemetryIntervalSteps { get; set; } = 2;

    /// <summary>Plant state persistence interval in steps.</summary>
    public int StatePersistenceIntervalSteps { get; set; } = 20;

    /// <summary>Consecutive failed supervisory polls before a controller is reported offline.</summary>
    public int SupervisoryTimeoutPolls { get; set; } = 3;

    public bool StartPaused { get; set; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!double.IsFinite(StepSeconds) || StepSeconds is <= 0 or > 10)
            errors.Add("Simulation:StepSeconds must be between 0 and 10.");
        if (!double.IsFinite(TimeScale) || TimeScale is < 0.1 or > 100)
            errors.Add("Simulation:TimeScale must be between 0.1 and 100.");
        if (TelemetryIntervalSteps < 1 || StatePersistenceIntervalSteps < 1 || SupervisoryTimeoutPolls < 1)
            errors.Add("Simulation intervals must be positive.");
        return errors;
    }
}
