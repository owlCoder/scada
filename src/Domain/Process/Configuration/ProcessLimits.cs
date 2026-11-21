namespace Domain.Process.Configuration;

public sealed record ProcessLimits(double MaxDischargePressureBar, double DeadheadTripSeconds, double DeadheadValvePercent);
