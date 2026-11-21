using Domain.Grid.Definition;
using Domain.Grid.Network;
using Domain.Grid.Snapshots;
using Domain.Process.Devices;
using Xunit;

namespace UnitTests.Grid;

public sealed class GridEngineVoltageProtectionTests
{
    [Fact]
    public Task EarthFaultPicksUp51NAndTheFeederBreakerTrips()
    {
        var world = new GridWorld(DefaultGridDefinition.Create(), 42, TimeSpan.FromHours(8));
        world.SetFault("F1-2", GridEquipmentFault.EarthFault, "instructor");
        world.Step(0.02);
        var during = world.Snapshot();
        Assert.True(world.Relays["QF1-51N"].PickedUp);
        Assert.True(during.Branch("F1-1")!.CurrentA > 50, "earth-fault current must exceed the 51N pickup");
        Assert.True(world.Switches["QF1"].Closed, "definite-time 51N (0.2 s) must not have tripped yet");
        world.Step(0.5);
        var after = world.Snapshot();
        Assert.False(world.Switches["QF1"].Closed);
        Assert.True(world.Relays["QF1-50"].Tripped || world.Relays["QF1-51"].Tripped || world.Relays["QF1-51N"].Tripped);
        Assert.Equal(0, after.Load("L11")!.CustomersSupplied);
        Assert.Contains(after.Alarms, a => a.Active && a.Type == GridAlarmType.ProtectionTrip);
        return Task.CompletedTask;
    }

    [Fact]
    public Task BusbarUndervoltage27TripsTheIncomerAfterItsDefiniteDelay()
    {
        var template = DefaultGridDefinition.Create();
        var d = template with { Sources = [template.Sources[0] with { VoltageSetpointPu = 0.75 }] };
        var world = new GridWorld(d, 42, TimeSpan.FromHours(8));
        Assert.True(world.Snapshot().Bus("MV")!.VoltagePu < 0.8);
        for (var i = 0; i < 4; i++) world.Step(1);
        Assert.True(world.Switches["Q0"].Closed, "27 with a 5 s definite delay must not trip before 5 s");
        Assert.True(world.Relays["MV-27"].PickedUp);
        world.Step(1);
        Assert.False(world.Switches["Q0"].Closed);
        Assert.True(world.Relays["MV-27"].Tripped);
        var snapshot = world.Snapshot();
        Assert.Equal(0, snapshot.Bus("MV")!.VoltageKv);
        Assert.Equal(0, snapshot.Totals.CustomersSupplied);
        Assert.Contains(snapshot.Alarms, a => a.Active && a.Type == GridAlarmType.ProtectionTrip && a.SourceId == "MV-27");
        Assert.Equal(snapshot.Loads.Count, snapshot.Alarms.Count(a => a.Active && a.Type == GridAlarmType.LossOfSupply));
        return Task.CompletedTask;
    }

    [Fact]
    public Task MildSagRaisesUndervoltageAlarmButDoesNotTrip27()
    {
        var template = DefaultGridDefinition.Create();
        var d = template with { Sources = [template.Sources[0] with { VoltageSetpointPu = 0.85 }] };
        var world = new GridWorld(d, 42, TimeSpan.FromHours(8));
        Assert.InRange(world.Snapshot().Bus("MV")!.VoltagePu, 0.85, 0.90);
        for (var i = 0; i < 3; i++) world.Step(1);
        var snapshot = world.Snapshot();
        Assert.Contains(snapshot.Alarms, a => a.Active && a.Type == GridAlarmType.Undervoltage && a.SourceId == "MV");
        Assert.False(world.Relays["MV-27"].PickedUp, "0.8 pu pickup must not operate at ~0.87 pu");
        Assert.True(world.Switches["Q0"].Closed);
        Assert.Equal(snapshot.Totals.Customers, snapshot.Totals.CustomersSupplied);
        return Task.CompletedTask;
    }

    [Fact]
    public Task UndervoltageAlarmClearsOnlyAboveTheHysteresisBand()
    {
        var template = DefaultGridDefinition.Create();
        var d = template with
        {
            Sources = [template.Sources[0] with { VoltageSetpointPu = 0.85 }],
            Buses = template.Buses.Select(b => b.Id == "MV" ? b with { MinVoltagePu = 0.90 } : b).ToArray()
        };
        var world = new GridWorld(d, 42, TimeSpan.FromHours(8));
        world.TapAutomatic["TR1"] = false;
        for (var i = 0; i < 3; i++) world.Step(1);
        var raised = world.Snapshot();
        Assert.True(raised.Bus("MV")!.VoltagePu < 0.90);
        Assert.Contains(raised.Alarms, a => a.Active && a.Type == GridAlarmType.Undervoltage && a.SourceId == "MV");
        // Lift the MV voltage into the hysteresis band [0.90, 0.91): the active alarm must persist.
        world.SetTap("TR1", 5, "ana", GridChangeCause.Operator);
        world.Step(1);
        var band = world.Snapshot();
        Assert.InRange(band.Bus("MV")!.VoltagePu, 0.90, 0.91);
        Assert.Contains(band.Alarms, a => a.Active && a.Type == GridAlarmType.Undervoltage && a.SourceId == "MV");
        // A fresh world at the same voltage is inside limits and must not raise the alarm.
        var fresh = new GridWorld(d, 42, TimeSpan.FromHours(8));
        fresh.TapAutomatic["TR1"] = false;
        fresh.SetTap("TR1", 5, "ana", GridChangeCause.Operator);
        for (var i = 0; i < 3; i++) fresh.Step(1);
        Assert.DoesNotContain(fresh.Snapshot().Alarms, a => a.Active && a.Type == GridAlarmType.Undervoltage && a.SourceId == "MV");
        // Lift clearly above the band: the alarm clears.
        world.SetTap("TR1", 8, "ana", GridChangeCause.Operator);
        world.Step(1);
        Assert.DoesNotContain(world.Snapshot().Alarms, a => a.Active && a.Type == GridAlarmType.Undervoltage && a.SourceId == "MV");
        return Task.CompletedTask;
    }

    [Fact]
    public Task OfflineRtuRaisesCommunicationLossAlarmAndBadQualityMeasurements()
    {
        var world = new GridWorld(DefaultGridDefinition.Create(), 42, TimeSpan.FromHours(8));
        world.SetRtuOnline("RTU3", false, "instructor");
        world.Step(1);
        var snapshot = world.Snapshot();
        Assert.Contains(snapshot.Alarms, a => a.Active && a.Type == GridAlarmType.CommunicationLoss && a.SourceId == "RTU3");
        var rtu3 = snapshot.Measurements.Where(m => m.RtuId == "RTU3").ToArray();
        Assert.NotEmpty(rtu3);
        Assert.All(rtu3, m => Assert.Equal(MeasurementQuality.Bad, m.Quality));
        world.SetRtuOnline("RTU3", true, "instructor");
        world.Step(1);
        var back = world.Snapshot();
        Assert.DoesNotContain(back.Alarms, a => a.Active && a.Type == GridAlarmType.CommunicationLoss && a.SourceId == "RTU3");
        Assert.All(back.Measurements.Where(m => m.RtuId == "RTU3"), m => Assert.Equal(MeasurementQuality.Good, m.Quality));
        return Task.CompletedTask;
    }
}
