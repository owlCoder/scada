using Domain.Grid.Definition;
using Domain.Grid.Definition.Templates;
using Xunit;

namespace UnitTests.Grid.Templates;

public sealed class GridTemplateCatalogTests
{
    public static TheoryData<string> TemplateIds => [.. GridTemplateCatalog.All.Select(e => e.Id)];

    [Fact]
    public Task CatalogListsDefaultAndBothNewTemplatesWithUniqueIds()
    {
        Assert.Equal(3, GridTemplateCatalog.All.Count);
        Assert.Equal(["default", UrbanCableGridTemplate.TemplateId, RuralOverheadGridTemplate.TemplateId],
            GridTemplateCatalog.All.Select(e => e.Id).ToArray());
        Assert.All(GridTemplateCatalog.All, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Name));
            Assert.False(string.IsNullOrWhiteSpace(e.Description));
            Assert.NotNull(e.Factory());
        });
        Assert.NotNull(GridTemplateCatalog.Find(UrbanCableGridTemplate.TemplateId));
        Assert.Null(GridTemplateCatalog.Find("no-such-template"));
        return Task.CompletedTask;
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public Task EveryTemplateIsValidatorClean(string templateId)
    {
        var definition = GridTemplateCatalog.Find(templateId)!.Factory();
        var issues = GridDefinitionValidator.Validate(definition);
        Assert.False(GridDefinitionValidator.HasErrors(issues), string.Join("; ", issues));
        // Only warnings may remain, and every one must be an RTU-assignment warning we accept deliberately.
        Assert.All(issues, issue => Assert.StartsWith("Warning:", issue, StringComparison.Ordinal));
        return Task.CompletedTask;
    }

    [Fact]
    public Task EveryTemplateAssignsEveryElementToAnRtuSoNoWarningsRemain()
    {
        foreach (var entry in GridTemplateCatalog.All)
        {
            var definition = entry.Factory();
            var issues = GridDefinitionValidator.Validate(definition);
            Assert.True(issues.Count == 0, $"{entry.Id}: {string.Join("; ", issues)}");
        }
        return Task.CompletedTask;
    }

    [Fact]
    public Task UrbanTemplateHasTheDescribedEquipment()
    {
        var d = UrbanCableGridTemplate.Create();
        Assert.Equal(2, d.Transformers.Count(t => t is { HasOnLoadTapChanger: true } && t.RatedPowerMva == 40));
        Assert.Equal(14, d.Loads.Count);
        Assert.Equal(16, d.Transformers.Count); // 2 station + 14 compact substations
        Assert.Equal(5, d.Switches.Count(s => s.Kind == GridSwitchKind.Breaker && d.Lines.Any(l => l.Id == s.ElementId))); // 4 feeders + bus coupler
        Assert.Equal(2, d.Switches.Count(s => s.Kind == GridSwitchKind.LoadBreakSwitch && !s.NormallyClosed)); // ring points
        Assert.Single(d.Capacitors);
        Assert.Equal(5, d.Rtus.Count);
        Assert.True(d.Loads.Count(l => l.Profile == GridLoadProfile.Commercial) >= 10, "commercial-heavy load");
        Assert.All(d.Lines, l => Assert.Equal(GridLineKind.Cable, l.Kind));
        return Task.CompletedTask;
    }

    [Fact]
    public Task RuralTemplateHasTheDescribedEquipment()
    {
        var d = RuralOverheadGridTemplate.Create();
        var oltc = Assert.Single(d.Transformers, t => t.HasOnLoadTapChanger);
        Assert.Equal(20, oltc.RatedPowerMva);
        Assert.Equal(10, d.Loads.Count);
        Assert.Equal(11, d.Transformers.Count); // 1 station + 10 pole-mounted
        Assert.Equal(3, d.Switches.Count(s => s.Kind == GridSwitchKind.Breaker && d.Lines.Any(l => l.Id == s.ElementId)));
        Assert.Equal(2, d.Generators.Count(g => g.Kind == GridGeneratorKind.Photovoltaic));
        Assert.True(d.Generators.Sum(g => g.RatedPowerMw) >= 5, "two large PV parks");
        Assert.Single(d.Capacitors);
        Assert.Equal(4, d.Rtus.Count);
        Assert.All(d.Lines, l => Assert.Equal(GridLineKind.Overhead, l.Kind));
        Assert.True(d.Lines.Sum(l => l.LengthKm) >= 50, "long overhead feeders");
        Assert.All(d.Loads, l => Assert.Equal(GridLoadProfile.Residential, l.Profile));
        // Reclosing-style feeder scheme: 50 + 51 + 51N on every feeder breaker, no reclose device.
        foreach (var qf in d.Switches.Where(s => s.Kind == GridSwitchKind.Breaker && d.Lines.Any(l => l.Id == s.ElementId)))
            Assert.Equal(3, d.Protections.Count(p => p.SwitchId == qf.Id));
        return Task.CompletedTask;
    }

    [Fact]
    public Task FactoriesReturnIndependentInstances()
    {
        var a = UrbanCableGridTemplate.Create();
        var b = UrbanCableGridTemplate.Create();
        Assert.NotSame(a, b);
        Assert.NotSame(a.Buses, b.Buses);
        Assert.Equal(a.Buses.Count, b.Buses.Count);
        return Task.CompletedTask;
    }
}
