namespace Domain;

public sealed class Plant
{
    private readonly object _sync = new();
    private double _tankLevel = 50;
    private bool _pumpRunning;
    private bool _outletValveOpen = true;
    private long _revision;

    public string Name => "Water Treatment Plant";
    public double TankCapacity => 100;

    public PlantState GetState()
    {
        lock (_sync)
            return new PlantState(Name, _tankLevel, TankCapacity, _pumpRunning, _revision,
                _outletValveOpen, _pumpRunning ? 0.7 : 0, _outletValveOpen ? 0.2 : 0);
    }

    public PlantState SetPump(bool running)
    {
        lock (_sync)
        {
            if (_pumpRunning == running)
                return GetState();

            if (running && _tankLevel >= TankCapacity)
                throw new InvalidOperationException("Cannot start the pump when the tank is full.");

            _pumpRunning = running;
            _revision++;
            return GetState();
        }
    }

    public PlantState SetOutletValve(bool open)
    {
        lock (_sync)
        {
            if (_outletValveOpen == open)
                return GetState();

            _outletValveOpen = open;
            _revision++;
            return GetState();
        }
    }

    public PlantState Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0 || seconds > 60)
            throw new ArgumentOutOfRangeException(nameof(seconds));

        lock (_sync)
        {
            var inflow = _pumpRunning ? 0.7 : 0;
            var outflow = _outletValveOpen ? 0.2 : 0;
            _tankLevel = Math.Clamp(_tankLevel + (inflow - outflow) * seconds, 0, TankCapacity);

            if (_tankLevel >= TankCapacity)
                _pumpRunning = false;

            _revision++;
            return GetState();
        }
    }
}
