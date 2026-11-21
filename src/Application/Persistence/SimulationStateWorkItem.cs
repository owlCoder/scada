using Application.Simulation;

namespace Application.Persistence;

public sealed record SimulationStateWorkItem(SimulationStateSnapshot State) : PersistenceWorkItem("plant-state");
