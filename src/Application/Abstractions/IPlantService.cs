using Domain;

namespace Application;

public interface IPlantService
{
    PlantState GetState();
    PlantState Advance(double seconds);
    PlantState SetPump(bool running, string actor);
    PlantState SetOutletValve(bool open, string actor);
}
