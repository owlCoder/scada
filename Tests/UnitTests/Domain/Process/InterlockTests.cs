using Domain.Process.Commands;
using Domain.Process.Devices;
using Xunit;

namespace UnitTests.Domain.Process;

public sealed class InterlockTests
{
    private static readonly CommandOrigin Operator = CommandOrigin.Operator("operator");

    [Fact]
    public Task OverfillProtectionBlocksPumpStart()
    {
        var plant = PlantTests.CreatePlant();
        plant.SetTankLevel("T-102", 96);
        plant.SetMode("P-101", ControlMode.Manual, Operator);

        var result = plant.CommandPump("P-101", true, Operator);

        Assert.Equal(CommandRejectionReason.InterlockActive, result.Reason);
        Assert.Contains("overfill", result.Message, StringComparison.OrdinalIgnoreCase);
        return Task.CompletedTask;
    }

    [Fact]
    public Task DryRunProtectionBlocksPumpStart()
    {
        var plant = PlantTests.CreatePlant();
        plant.SetTankLevel("T-101", 5);
        plant.SetMode("P-101", ControlMode.Manual, Operator);

        Assert.Equal(CommandRejectionReason.InterlockActive, plant.CommandPump("P-101", true, Operator).Reason);
        return Task.CompletedTask;
    }

    [Fact]
    public Task RunningPumpTripsAtHighHighAndStaysLatched()
    {
        var plant = PlantTests.CreatePlant();
        plant.SetTankLevel("T-102", 94.8);
        plant.SetMode("P-101", ControlMode.Manual, Operator);
        Assert.True(plant.CommandPump("P-101", true, Operator).Accepted);

        PlantTests.Run(plant, 30);

        var pump = plant.FindPump("P-101")!;
        Assert.False(pump.IsCommandedOn);
        Assert.NotNull(pump.InterlockReason);
        plant.SetTankLevel("T-102", 50);
        Assert.Equal(CommandRejectionReason.InterlockActive, plant.CommandPump("P-101", true, Operator).Reason);
        Assert.True(plant.ResetInterlock("P-101").Accepted);
        Assert.True(plant.CommandPump("P-101", true, Operator).Accepted);
        return Task.CompletedTask;
    }

    [Fact]
    public Task BypassedInterlocksRecordViolationsInsteadOfTripping()
    {
        var plant = PlantTests.CreatePlant();
        plant.SetTankLevel("T-102", 96);
        Assert.True(plant.SetInterlockBypass(true, "instructor").Accepted);
        Assert.True(plant.CommandPump("P-101", true, CommandOrigin.Unattributed("intruder")).Accepted);

        var step = plant.Step(TimeSpan.FromSeconds(0.5), DateTimeOffset.UnixEpoch);

        Assert.Contains(step.SafetyViolations, v => v.DeviceId == "P-101");
        Assert.Empty(step.Interlocks);
        Assert.True(plant.FindPump("P-101")!.IsCommandedOn);
        return Task.CompletedTask;
    }
}
