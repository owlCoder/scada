using Application.Simulation;
using Domain.Process.Snapshots;

namespace Infrastructure.Persistence.Postgres.Entities;

/// <summary>Single-row table that holds the newest simulation snapshot.</summary>
public sealed class PlantStateEntity
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public DateTimeOffset SavedAt { get; set; }
    public bool IsPaused { get; set; }
    public string Plant { get; set; } = "{}";
    public string Controllers { get; set; } = "[]";

    public PlantStateEntity Apply(SimulationStateSnapshot state)
    {
        SavedAt = Timestamps.Utc(state.SavedAt);
        IsPaused = state.IsPaused;
        Plant = PersistenceJson.Serialize(state.Plant);
        Controllers = PersistenceJson.Serialize(state.Controllers);
        return this;
    }

    public SimulationStateSnapshot ToRecord() => new(SavedAt, PersistenceJson.Deserialize<PlantSnapshot>(Plant),
        PersistenceJson.Deserialize<ControllerStateRecord[]>(Controllers), IsPaused);
}
