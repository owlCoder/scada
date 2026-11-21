namespace Security.Scenarios;

/// <summary>An action executed once the run has been running for <see cref="AtSeconds"/> simulated seconds.</summary>
public sealed record ScheduledScenarioAction(double AtSeconds, IScenarioAction Action);
