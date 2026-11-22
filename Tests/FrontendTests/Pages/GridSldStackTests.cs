using Domain.Grid.Definition;
using Domain.Grid.Definition.Templates;
using FrontendTests.Support;
using Hmi.Services.Grid.Sld;
using Xunit;

namespace FrontendTests.Pages;

public sealed class GridSldStackTests
{
    [Fact]
    public Task DistributionSubstationsFormOneCompactAlignedVerticalStackUnderTheFeeder()
    {
        var definition = DefaultGridDefinition.Create();
        var render = SldModelBuilder.RenderPositions(definition);
        // TS11: MV junction F1-1-MV on the feeder row, transformer TS11, LV bus F1-1-LV, load L11.
        var mv = render["F1-1-MV"];
        var tr = render["TS11"];
        var lv = render["F1-1-LV"];
        var load = render["L11"];
        Assert.Equal(mv.X, tr.X, 3);
        Assert.Equal(mv.X, lv.X, 3);
        Assert.Equal(mv.X, load.X, 3); // all four aligned in one column
        Assert.True(mv.Y < tr.Y && tr.Y < lv.Y && lv.Y < load.Y, "stack runs downward from the feeder line");
        var gap1 = tr.Y - mv.Y;
        var gap2 = lv.Y - tr.Y;
        var gap3 = load.Y - lv.Y;
        Assert.Equal(gap1, gap2, 3);
        Assert.Equal(gap2, gap3, 3); // consistent spacing
        Assert.InRange(gap1, SldStackLayout.MinRowGap, SldStackLayout.DesignRowGap);
        // The stack stays clear of the next feeder row below (feeder 2 at grid y=7 -> 392 SVG units).
        Assert.True(load.Y < 7 * SldModelBuilder.UnitY - 10, $"stack bottom {load.Y} collides with the next feeder row");
        return Task.CompletedTask;
    }

    [Fact]
    public Task EveryDistributionSubstationOfBothTemplatesIsAlignedWithConsistentGaps()
    {
        foreach (var create in new Func<GridDefinition>[] { DefaultGridDefinition.Create, UrbanCableGridTemplate.Create, RuralOverheadGridTemplate.Create })
        {
            var definition = create();
            var render = SldModelBuilder.RenderPositions(definition);
            var buses = definition.Buses.ToDictionary(b => b.Id);
            foreach (var transformer in definition.Transformers)
            {
                if (!buses.TryGetValue(transformer.LvBusId, out var lv) || lv.NominalVoltageKv >= 1)
                    continue;
                var loads = definition.Loads.Where(l => l.BusId == lv.Id).ToArray();
                Assert.NotEmpty(loads);
                var mv = render[transformer.HvBusId];
                var tr = render[transformer.Id];
                var lvPos = render[lv.Id];
                Assert.Equal(mv.X, tr.X, 3);
                Assert.Equal(mv.X, lvPos.X, 3);
                var gap = tr.Y - mv.Y;
                Assert.Equal(gap, lvPos.Y - tr.Y, 3);
                Assert.InRange(gap, SldStackLayout.MinRowGap, SldStackLayout.DesignRowGap);
                foreach (var load in loads)
                {
                    var loadPos = render[load.Id];
                    Assert.Equal(mv.X, loadPos.X, 3);
                    Assert.True(loadPos.Y > lvPos.Y, "load sits below the LV bus in the stack");
                }
            }
        }
        return Task.CompletedTask;
    }

    [Fact]
    public Task PrimarySubstationsAndGridsWithout04KvKeepTheDefinitionPositions()
    {
        var definition = new HmiFakeGridView().Definition; // no 0.4 kV consumer bus -> no stacking
        var render = SldModelBuilder.RenderPositions(definition);
        Assert.Empty(SldStackLayout.Overrides(definition, render));
        foreach (var bus in definition.Buses)
            Assert.Equal(SldModelBuilder.At(bus.Position), render[bus.Id]);
        foreach (var transformer in definition.Transformers)
            Assert.Equal(SldModelBuilder.At(transformer.Position), render[transformer.Id]);
        return Task.CompletedTask;
    }

    [Fact]
    public Task ASubstationTheUserAlreadySpacedOutIsKeptAsTheBase()
    {
        var definition = GridProtectionDefaults.Add(new GridDefinition("spaced", 50,
            [new("B-MV", "MV", 20, GridBusKind.Busbar, new(0, 0)), new("B-LV", "LV", 0.4, GridBusKind.ConsumerPoint, new(0, 4))],
            [new("SRC", "grid", "B-MV", 1, 100, new(0, -2))],
            [],
            [new("TR", "20/0.4 kV", "B-MV", "B-LV", 0.63, 20, 0.4, 6, 6.5, 1, false, 0, 0, 0, 0, 0, null, 0, 0, "Dyn11", new(0, 2))],
            [],
            [new("LD", "load", "B-LV", 0.38, 0.96, GridLoadProfile.Residential, 190, false, new(0, 6))],
            [], [], [], []));
        var render = SldModelBuilder.RenderPositions(definition);
        Assert.Empty(SldStackLayout.Overrides(definition, render)); // span 4 units = 224 SVG >= 2 design rows, aligned
        Assert.Equal(SldModelBuilder.At(new DiagramPosition(0, 2)), render["TR"]);
        Assert.Equal(SldModelBuilder.At(new DiagramPosition(0, 4)), render["B-LV"]);
        Assert.Equal(SldModelBuilder.At(new DiagramPosition(0, 6)), render["LD"]);
        return Task.CompletedTask;
    }
}
