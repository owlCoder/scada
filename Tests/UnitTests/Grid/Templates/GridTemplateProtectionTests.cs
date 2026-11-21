using Application.Grid;
using Domain.Grid.Definition;
using Domain.Grid.Definition.Templates;
using Domain.Grid.Snapshots;
using Xunit;

namespace UnitTests.Grid.Templates;

public sealed class GridTemplateProtectionTests
{
    public static TheoryData<string> TemplateIds => [.. GridTemplateCatalog.All.Select(e => e.Id)];

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public Task OvercurrentPickupsStayAboveMaximumLoadCurrentsOverADay(string templateId)
    {
        var definition = GridTemplateCatalog.Find(templateId)!.Factory();
        var engine = new GridEngine(definition, new GridOptions { StartTimeOfDay = "00:00", StepSeconds = 60 }, TimeProvider.System);
        var maxCurrent = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var i = 0; i < 1440; i++)
        {
            engine.Step();
            foreach (var branch in engine.Snapshot().Branches)
                maxCurrent[branch.Id] = Math.Max(maxCurrent.GetValueOrDefault(branch.Id), branch.CurrentA);
        }
        var overcurrent = definition.Protections
            .Where(p => p.Function is GridProtectionFunction.Overcurrent51 or GridProtectionFunction.InstantaneousOvercurrent50)
            .ToArray();
        Assert.NotEmpty(overcurrent);
        foreach (var protection in overcurrent)
        {
            var elementId = definition.Switches.First(s => s.Id == protection.SwitchId).ElementId;
            var max = maxCurrent.GetValueOrDefault(elementId);
            Assert.True(protection.PickupValue > max,
                $"{protection.Id}: pickup {protection.PickupValue:0.#} A must exceed the maximum load current " +
                $"{max:0.#} A on {elementId} over the day");
        }
        return Task.CompletedTask;
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public Task FeederRelaysCoordinateWithTheIncomerBackup(string templateId)
    {
        var definition = GridTemplateCatalog.Find(templateId)!.Factory();
        var feederBreakers = definition.Switches
            .Where(s => s.Kind == GridSwitchKind.Breaker && definition.Lines.Any(l => l.Id == s.ElementId))
            .Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var feederBreaker in feederBreakers)
        {
            var feeder51 = definition.Protections.FirstOrDefault(p => p.SwitchId == feederBreaker
                && p.Function == GridProtectionFunction.Overcurrent51);
            Assert.NotNull(feeder51);
            // Backup incomer 51 must be graded above every feeder 51.
            foreach (var backup in definition.Protections.Where(p => p.Function == GridProtectionFunction.Overcurrent51
                         && !feederBreakers.Contains(p.SwitchId)))
                Assert.True(backup.PickupValue > feeder51.PickupValue,
                    $"{backup.Id} pickup {backup.PickupValue} A should exceed feeder {feeder51.Id} pickup {feeder51.PickupValue} A");
        }
        return Task.CompletedTask;
    }
}
