namespace Domain;

public sealed class Plant
{
    private readonly object _sync = new();
    private double _tankLevel = 50;
    private bool _pumpRunning;
    private long _revision;

    public string Name => "Постројење за воду";
    public double TankCapacity => 100;

    public PlantState GetState()
    {
        lock (_sync)
            return new PlantState(Name, _tankLevel, TankCapacity, _pumpRunning, _revision);
    }

    public void SetPump(bool running)
    {
        lock (_sync)
        {
            _pumpRunning = running;
            _revision++;
        }
    }

    public void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0 || seconds > 60)
            throw new ArgumentOutOfRangeException(nameof(seconds));

        lock (_sync)
        {
            var change = (_pumpRunning ? 0.7 : 0) - 0.2;
            _tankLevel = Math.Clamp(_tankLevel + change * seconds, 0, TankCapacity);
            _revision++;
        }
    }
}

public sealed record PlantState(string Name, double TankLevel, double TankCapacity, bool PumpRunning, long Revision);
