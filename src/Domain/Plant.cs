namespace Domain;

public sealed class Plant
{
    public string Name { get; } = "Постројење за воду";
    public double TankLevel { get; private set; } = 50;
    public double TankCapacity { get; } = 100;
    public bool PumpRunning { get; private set; }
    public long Revision { get; private set; }

    public void SetPump(bool running)
    {
        PumpRunning = running;
        Revision++;
    }

    public void Advance(double seconds)
    {
        if (seconds <= 0 || seconds > 60)
            throw new ArgumentOutOfRangeException(nameof(seconds));

        var change = (PumpRunning ? 0.7 : 0) - 0.2;
        TankLevel = Math.Clamp(TankLevel + change * seconds, 0, TankCapacity);
        Revision++;
    }
}
