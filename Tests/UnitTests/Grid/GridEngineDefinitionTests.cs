using Application.Grid;
using Domain.Grid.Definition;
using Infrastructure.Persistence.InMemory;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace UnitTests.Grid;

public sealed class GridEngineDefinitionTests
{
    [Fact]
    public Task DefaultDefinitionIsValidAndHasExpectedEquipment()
    {
        var d = DefaultGridDefinition.Create();
        Assert.Empty(GridDefinitionValidator.Validate(d));
        Assert.Equal(12, d.Transformers.Count);
        Assert.Equal(4, d.Rtus.Count);
        Assert.Equal(11, d.Loads.Count);
        Assert.Equal([1, 2, 3, 4], d.Rtus.Select(r => r.CommonAddress));
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("reference")]
    [InlineData("lineVoltage")]
    [InlineData("transformerVoltage")]
    [InlineData("switchTerminal")]
    [InlineData("protectionSwitch")]
    [InlineData("noSource")]
    [InlineData("rtu")]
    [InlineData("parameter")]
    [InlineData("nan")]
    [InlineData("tap")]
    [InlineData("enum")]
    public Task InvalidDefinitionsAreRejected(string issue)
    {
        var d = DefaultGridDefinition.Create();
        d = issue switch
        {
            "duplicate" => d with { Buses = [.. d.Buses, d.Buses[0]] },
            "reference" => d with { Loads = [d.Loads[0] with { BusId = "missing" }] },
            "lineVoltage" => d with { Lines = [d.Lines[0] with { FromBusId = "HV" }] },
            "transformerVoltage" => d with { Transformers = [d.Transformers[0] with { HvRatedKv = 220 }] },
            "switchTerminal" => d with { Switches = [d.Switches[0] with { BusId = "HV" }] },
            "protectionSwitch" => d with { Protections = [d.Protections[0] with { SwitchId = "DS0" }] },
            "noSource" => d with { Sources = [] },
            "rtu" => d with { Rtus = [d.Rtus[0] with { ElementIds = ["missing"] }] },
            "parameter" => d with { Lines = [d.Lines[0] with { LengthKm = -1 }] },
            "nan" => d with { Sources = [d.Sources[0] with { ShortCircuitPowerMva = double.NaN }] },
            "tap" => d with { Transformers = [d.Transformers[0] with { InitialTap = 20 }] },
            _ => d with { Protections = [d.Protections[0] with { Curve = (GridCurveType)999 }] }
        };
        Assert.True(GridDefinitionValidator.HasErrors(GridDefinitionValidator.Validate(d)));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task WarningsDoNotBlockSavingAndVersionsAreImmutable()
    {
        var time = new FakeTimeProvider();
        var store = new InMemoryGridDefinitionStore(time);
        var d = DefaultGridDefinition.Create() with { Rtus = [] };
        var issues = GridDefinitionValidator.Validate(d);
        Assert.NotEmpty(issues);
        Assert.False(GridDefinitionValidator.HasErrors(issues));
        var initial = await store.GetActiveAsync(CancellationToken.None);
        Assert.Equal(1, initial!.Version);
        var saved = await store.SaveAsync(d, "instructor", "test", CancellationToken.None);
        Assert.Equal(2, saved.Version);
        Assert.False(saved.Active);
        ((IList<GridBusDefinition>)saved.Definition.Buses)[0] = saved.Definition.Buses[0] with { Name = "mutated" };
        Assert.NotEqual("mutated", (await store.GetAsync(2, CancellationToken.None))!.Definition.Buses[0].Name);
        Assert.True(await store.SetActiveAsync(2, CancellationToken.None));
        Assert.False(await store.SetActiveAsync(999, CancellationToken.None));
        Assert.Equal(2, (await store.GetActiveAsync(CancellationToken.None))!.Version);
        Assert.Single(await store.ListAsync(CancellationToken.None), v => v.Active);
    }

    [Fact]
    public async Task ConcurrentMemorySavesAllocateDistinctVersions()
    {
        var store = new InMemoryGridDefinitionStore(TimeProvider.System);
        var versions = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => store.SaveAsync(DefaultGridDefinition.Create(), "instructor", i.ToString(), CancellationToken.None)));
        Assert.Equal(Enumerable.Range(2, 10), versions.Select(x => x.Version).Order());
    }

    [Theory]
    [InlineData("25:00", 1, 60, 30)]
    [InlineData("08:00", 0, 60, 30)]
    [InlineData("08:00", 1, 0, 30)]
    [InlineData("08:00", 1, 60, 0)]
    public Task InvalidOptionsAreRejected(string start, double step, double scale, double timeout)
    {
        var options = new GridOptions { StartTimeOfDay = start, StepSeconds = step, TimeScale = scale, SelectTimeoutSeconds = timeout };
        Assert.NotEmpty(options.Validate());
        return Task.CompletedTask;
    }
}
