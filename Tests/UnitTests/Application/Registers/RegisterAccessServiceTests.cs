using Application.Events;
using Application.Identity;
using Application.Registers;
using Domain.Control;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Application.Registers;

public sealed class RegisterAccessServiceTests
{
    private static readonly Actor Ana = new("ana", AccessRole.Operator);

    [Fact]
    public Task ControllersAreExposedAsOneBasedUnits()
    {
        var rig = new TestRig();
        var units = rig.Registers.ListControllers();

        Assert.Equal(["PLC-1", "PLC-2", "RTU-1"], units.Select(u => u.ControllerId));
        Assert.Equal([1, 2, 3], units.Select(u => (int)u.UnitId));
        Assert.Null(rig.Registers.ControllerForUnit(0));
        Assert.Null(rig.Registers.ControllerForUnit(4));
        return Task.CompletedTask;
    }

    [Fact]
    public Task SetpointRegistersHoldTheConfiguredLevels()
    {
        var rig = new TestRig();
        var read = rig.Registers.Read(1, RegisterArea.Holding, ControllerRegisterMap.SetpointBase, 2, "test");

        Assert.Equal(RegisterOperationStatus.Success, read.Status);
        Assert.Equal(new ushort[] { 400, 750 }, read.Values);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(9, 0, 1, RegisterOperationStatus.UnknownUnit)]
    [InlineData(1, 250, 10, RegisterOperationStatus.IllegalAddress)]
    [InlineData(1, 0, 0, RegisterOperationStatus.IllegalValue)]
    [InlineData(1, 0, 126, RegisterOperationStatus.IllegalValue)]
    public Task InvalidReadsAreRejectedAndAudited(byte unit, ushort start, ushort count, RegisterOperationStatus expected)
    {
        var rig = new TestRig();

        Assert.Equal(expected, rig.Registers.Read(unit, RegisterArea.Holding, start, count, "modbus:test").Status);
        Assert.Contains(rig.Buffer.GetRecent(), e => e.Category == EventCategories.Protocol && e.Source == "modbus:test");
        return Task.CompletedTask;
    }

    [Fact]
    public Task WritesAreDeniedWhileDisabled()
    {
        var rig = new TestRig();

        var result = rig.Registers.WriteSetpoint(1, 100, 500, Ana, "test");

        Assert.Equal(RegisterOperationStatus.Denied, result.Status);
        Assert.Equal((ushort)400, rig.Registers.Read(1, RegisterArea.Holding, 100, 1, "test").Values[0]);
        return Task.CompletedTask;
    }

    [Fact]
    public Task EnabledWritesChangeOnlySetpointsWithinLimitsForOperators()
    {
        var rig = new TestRig(new RegisterWriteOptions { WritesEnabled = true });

        Assert.Equal(RegisterOperationStatus.Success, rig.Registers.WriteSetpoint(1, 100, 500, Ana, "test").Status);
        Assert.Equal((ushort)500, rig.Registers.Read(1, RegisterArea.Holding, 100, 1, "test").Values[0]);
        Assert.Equal(RegisterOperationStatus.IllegalAddress, rig.Registers.WriteSetpoint(1, 5, 1, Ana, "test").Status);
        Assert.Equal(RegisterOperationStatus.IllegalAddress, rig.Registers.WriteSetpoint(1, 200, 1, Ana, "test").Status);
        Assert.Equal(RegisterOperationStatus.IllegalValue, rig.Registers.WriteSetpoint(1, 100, 1001, Ana, "test").Status);
        Assert.Equal(RegisterOperationStatus.Denied,
            rig.Registers.WriteSetpoint(1, 100, 500, new Actor("guest", AccessRole.Viewer), "test").Status);
        Assert.Equal(RegisterOperationStatus.Denied, rig.Registers.WriteSetpoint(1, 100, 500, null, "test").Status);
        Assert.Contains(rig.Buffer.GetRecent(), e => e.Category == EventCategories.Audit && e.Message.Contains("HR100 changed 400 -> 500"));
        return Task.CompletedTask;
    }
}
