namespace Security.Grid.Scenarios;

/// <summary>An action executed once the run has been running for <see cref="AtSeconds"/> simulated seconds.</summary>
public sealed record ScheduledGridScenarioAction(double AtSeconds, GridScenarioActionSpec Spec);
