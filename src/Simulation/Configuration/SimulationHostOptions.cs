namespace Simulation.Configuration;

public sealed class SimulationHostOptions
{
    /// <summary>Optional JSON file describing plant, network and controllers. The default topology is used when empty.</summary>
    public string? TopologyFile { get; set; }

    /// <summary>Optional scenario id started once the host is up.</summary>
    public string? InitialScenario { get; set; }

    /// <summary>When false the simulation loop is not started (tests drive steps explicitly).</summary>
    public bool RunLoop { get; set; } = true;

    /// <summary>When false the latest persisted state is not restored at start-up.</summary>
    public bool RestoreOnStart { get; set; } = true;
}
