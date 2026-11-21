using Application.Simulation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Simulation.Configuration;
using Simulation.HostedServices;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Simulation.Configuration;

public sealed class SimulationRecoveryTests
{
    [Fact]
    public async Task StateSavedOnShutdownIsRestoredOnStart()
    {
        var rig = new TestRig();
        rig.Training.SetTankLevel("T-103", 77);
        await rig.RunForAsync(30);
        var first = new SimulationRecoveryService(rig.Engine, rig.PlantStateStore, new SimulationHostOptions(),
            NullLogger<SimulationRecoveryService>.Instance);
        await first.StopAsync(CancellationToken.None);
        var saved = rig.Engine.Status;

        var restarted = new SimulationEngine(SimulationTopology.Default(), new SimulationOptions(), new FakeTimeProvider());
        await new SimulationRecoveryService(restarted, rig.PlantStateStore, new SimulationHostOptions(),
            NullLogger<SimulationRecoveryService>.Instance).StartAsync(CancellationToken.None);

        Assert.Equal(saved.SimulationTime, restarted.Status.SimulationTime);
        Assert.Equal(saved.StepCount, restarted.Status.StepCount);
        Assert.Equal(rig.Training.Snapshot().Tank("T-103")!.Volume,
            restarted.Read(w => w.Plant.CaptureSnapshot()).Tank("T-103")!.Volume, 6);
    }

    [Fact]
    public async Task RestoreCanBeDisabled()
    {
        var rig = new TestRig();
        await rig.RunForAsync(10);
        await rig.PlantStateStore.SaveAsync(rig.Engine.CaptureState(), CancellationToken.None);

        var fresh = new SimulationEngine(SimulationTopology.Default(), new SimulationOptions(), new FakeTimeProvider());
        await new SimulationRecoveryService(fresh, rig.PlantStateStore, new SimulationHostOptions { RestoreOnStart = false },
            NullLogger<SimulationRecoveryService>.Instance).StartAsync(CancellationToken.None);

        Assert.Equal(0, fresh.Status.StepCount);
    }
}
