using Domain;

namespace Application;

public sealed class PlantSnapshotService(IPlantService plant)
{
    public PlantState GetSnapshot() => plant.GetState();
}
