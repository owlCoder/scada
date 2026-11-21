using Application.Scenarios;
using Application.Simulation;
using Domain.Process.Snapshots;

namespace Api.Realtime;

public sealed record HubSnapshot(PlantSnapshot Plant, SimulationStatus Simulation, ScenarioRunRecord? Scenario, DateTimeOffset At);
