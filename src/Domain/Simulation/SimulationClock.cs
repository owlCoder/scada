namespace Domain.Simulation;

/// <summary>Deterministic simulated time. The clock only moves when the engine steps it.</summary>
public sealed class SimulationClock
{
    public TimeSpan SimulationTime { get; private set; }
    public long StepCount { get; private set; }
    public bool IsPaused { get; private set; }

    public TimeSpan Advance(TimeSpan step)
    {
        if (step <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(step));

        SimulationTime += step;
        StepCount++;
        return SimulationTime;
    }

    public bool Pause()
    {
        var changed = !IsPaused;
        IsPaused = true;
        return changed;
    }

    public bool Resume()
    {
        var changed = IsPaused;
        IsPaused = false;
        return changed;
    }

    public bool Restore(TimeSpan simulationTime, long stepCount)
    {
        if (simulationTime < TimeSpan.Zero || stepCount < 0)
            return false;

        SimulationTime = simulationTime;
        StepCount = stepCount;
        return true;
    }
}
