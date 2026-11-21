using Application.Grid;
using Domain.Grid.Definition;
using Domain.Grid.Definition.Templates;
using Domain.Grid.Snapshots;
using Security.Grid.Scenarios;
using Security.Grid.Scenarios.Catalog;
using Xunit;

namespace UnitTests.Grid.Templates;

public sealed class GridTemplateScenarioTargetTests
{
    public static TheoryData<string> TemplateIds => [.. GridTemplateCatalog.All.Select(e => e.Id)];

    [Fact]
    public Task TheCatalogHasEightAttackScenarios()
    {
        Assert.Equal(8, DefaultGridScenarios.Create().Length);
        return Task.CompletedTask;
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public Task EveryAttackScenarioResolvesAllItsTargetsOnTheTemplate(string templateId)
    {
        var definition = GridTemplateCatalog.Find(templateId)!.Factory();
        var engine = new GridEngine(definition, new GridOptions(), TimeProvider.System);
        var scenarios = DefaultGridScenarios.Create();
        var targets = scenarios.SelectMany(s => s.RequiredTargets()).Distinct().ToArray();
        Assert.NotEmpty(targets);
        var resolution = GridScenarioTargetResolver.Resolve(engine, targets);
        Assert.True(resolution.Succeeded, string.Join("; ", resolution.Errors));
        var snapshot = engine.Snapshot();
        var knownIds = new HashSet<string>(AllIds(definition), StringComparer.Ordinal);
        knownIds.UnionWith(snapshot.Measurements.Select(m => m.Id));
        Assert.All(targets, target =>
        {
            var ids = resolution.Targets!.Resolve(target);
            Assert.NotEmpty(ids);
            // Targets resolve either to element ids or to measurement ids present in the live snapshot.
            Assert.All(ids, id => Assert.Contains(id, knownIds));
        });
        return Task.CompletedTask;
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public Task EachScenarioIndividuallyResolvesOnTheTemplate(string templateId)
    {
        var definition = GridTemplateCatalog.Find(templateId)!.Factory();
        var engine = new GridEngine(definition, new GridOptions(), TimeProvider.System);
        foreach (var scenario in DefaultGridScenarios.Create())
        {
            var resolution = GridScenarioTargetResolver.Resolve(engine, scenario.RequiredTargets().ToArray());
            Assert.True(resolution.Succeeded, $"{scenario.Id}: {string.Join("; ", resolution.Errors)}");
        }
        return Task.CompletedTask;
    }

    private static string[] AllIds(GridDefinition d) =>
        [.. d.Buses.Select(x => x.Id), .. d.Sources.Select(x => x.Id), .. d.Lines.Select(x => x.Id),
            .. d.Transformers.Select(x => x.Id), .. d.Switches.Select(x => x.Id), .. d.Loads.Select(x => x.Id),
            .. d.Generators.Select(x => x.Id), .. d.Capacitors.Select(x => x.Id), .. d.Protections.Select(x => x.Id),
            .. d.Rtus.Select(x => x.Id)];
}
