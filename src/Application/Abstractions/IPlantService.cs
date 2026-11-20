using Domain;

namespace Application;

public interface IPlantService
{
    PlantState GetState();
    void Advance(double seconds);
    void SetPump(bool running, string actor);
    void SetOutletValve(bool open, string actor);
}
