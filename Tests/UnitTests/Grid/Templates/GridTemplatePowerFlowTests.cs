using Application.Grid;
using Domain.Grid.Definition.Templates;
using Xunit;

namespace UnitTests.Grid.Templates;

public sealed class GridTemplatePowerFlowTests
{
    public static TheoryData<string, string> OperatingPoints => new()
    {
        { UrbanCableGridTemplate.TemplateId, "08:00" },
        { UrbanCableGridTemplate.TemplateId, "19:00" },
        { RuralOverheadGridTemplate.TemplateId, "08:00" },
        { RuralOverheadGridTemplate.TemplateId, "19:00" }
    };

    [Theory]
    [MemberData(nameof(OperatingPoints))]
    public Task TemplateConvergesWithinLimitsWithNoOverloadsAndNoAlarms(string templateId, string start)
    {
        var definition = GridTemplateCatalog.Find(templateId)!.Factory();
        var engine = new GridEngine(definition, new GridOptions { StartTimeOfDay = start }, TimeProvider.System);
        for (var i = 0; i < 10; i++) engine.Step();
        var s = engine.Snapshot();
        Assert.True(s.Converged);
        Assert.InRange(s.SolverIterations, 1, 30);
        Assert.All(s.Buses, b =>
        {
            if (b.Energized) Assert.InRange(b.VoltagePu, 0.95, 1.05);
        });
        Assert.All(s.Branches, b => Assert.InRange(b.LoadingPercent, 0, 100));
        Assert.DoesNotContain(s.Alarms, a => a.Active);
        Assert.Equal(s.Totals.Customers, s.Totals.CustomersSupplied);
        return Task.CompletedTask;
    }

    [Fact]
    public Task UrbanTemplateSplitsLoadOverBothTransformersAndRings()
    {
        var engine = new GridEngine(UrbanCableGridTemplate.Create(), new GridOptions { StartTimeOfDay = "10:00" }, TimeProvider.System);
        engine.Step();
        var s = engine.Snapshot();
        var tr1 = s.Branch("TR1")!; var tr2 = s.Branch("TR2")!;
        Assert.InRange(tr1.LoadingPercent, 1, 60);
        Assert.InRange(tr2.LoadingPercent, 1, 60);
        // Both ring points are open, so every substation is supplied radially from its own feeder.
        Assert.False(s.Switch("NOP-A")!.Closed);
        Assert.False(s.Switch("NOP-B")!.Closed);
        Assert.True(s.Bus("SS23-MV")!.Energized);
        Assert.Equal(s.Totals.Customers, s.Totals.CustomersSupplied);
        return Task.CompletedTask;
    }

    [Fact]
    public Task RuralTemplateShowsReversePowerAtMiddayButStaysWithinLimits()
    {
        // Deliberate exception: at midday the two PV parks inject more than the rural load, so power flows back
        // towards the 110 kV infeed. Voltages and loadings must still stay inside limits.
        var engine = new GridEngine(RuralOverheadGridTemplate.Create(), new GridOptions { StartTimeOfDay = "12:00" }, TimeProvider.System);
        for (var i = 0; i < 10; i++) engine.Step();
        var s = engine.Snapshot();
        Assert.True(s.Converged);
        Assert.True(s.Totals.GenerationMw > s.Totals.LoadMw,
            $"expected reverse power at midday, generation {s.Totals.GenerationMw:0.##} MW vs load {s.Totals.LoadMw:0.##} MW");
        Assert.True(s.Totals.SourceImportMw < 0, $"expected export to the upstream grid, got {s.Totals.SourceImportMw:0.##} MW");
        Assert.All(s.Buses, b => { if (b.Energized) Assert.InRange(b.VoltagePu, 0.9, 1.1); });
        Assert.All(s.Branches, b => Assert.InRange(b.LoadingPercent, 0, 100));
        Assert.DoesNotContain(s.Alarms, a => a.Active);
        return Task.CompletedTask;
    }

    [Fact]
    public Task RuralAvrKeepsTheTapInsideItsRangeOverADay()
    {
        var engine = new GridEngine(RuralOverheadGridTemplate.Create(),
            new GridOptions { StartTimeOfDay = "00:00", StepSeconds = 60 }, TimeProvider.System);
        var min = int.MaxValue; var max = int.MinValue;
        for (var i = 0; i < 1440; i++)
        {
            engine.Step();
            var tap = engine.Snapshot().Branch("TR1")!.TapPosition!.Value;
            min = Math.Min(min, tap); max = Math.Max(max, tap);
        }
        var tr = engine.Definition.Transformers.Single(t => t.Id == "TR1");
        Assert.InRange(min, tr.TapMin + 1, tr.TapMax - 1);
        Assert.InRange(max, tr.TapMin + 1, tr.TapMax - 1);
        Assert.All(engine.Snapshot().Buses, b => { if (b.Energized) Assert.InRange(b.VoltagePu, 0.9, 1.1); });
        return Task.CompletedTask;
    }
}
