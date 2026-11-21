using Domain.Grid.Definition;

namespace Domain.Grid.Network;

public sealed class GridRelayState(GridProtectionDefinition definition)
{
    public GridProtectionDefinition Settings { get; set; } = definition;
    public long LastTripOperation { get; set; } = -1;
    public double Progress { get; set; }
    public bool PickedUp { get; set; }
    public bool Tripped { get; set; }
    public TimeSpan? LastTripAt { get; set; }
}
