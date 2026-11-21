using DatabaseTests.Support;
using Domain.Grid.Definition;
using Infrastructure.Persistence.Postgres.Stores;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace DatabaseTests.Stores;

[Collection(PostgresCollection.Name)]
public sealed class GridEngineDefinitionStoreTests(PostgresContainerFixture postgres)
{
    [Fact]
    public async Task SeedsVersionsRoundTripsAndKeepsExactlyOneActiveDefinition()
    {
        var database = await postgres.CreateDatabaseAsync();
        var store = new PostgresGridDefinitionStore(await database.MigratedAsync(), new FakeTimeProvider(Samples.Start));
        var none = CancellationToken.None;
        var seed = await store.GetActiveAsync(none);
        Assert.Equal(1, seed!.Version);
        Assert.Equal(11, seed.Definition.Loads.Count);
        var saved = await store.SaveAsync(seed.Definition with { Name = "Database exercise" }, "instructor", "comment", none);
        Assert.Equal(2, saved.Version);
        Assert.Equal("instructor", saved.CreatedBy);
        Assert.Equal(Samples.Start, saved.CreatedAt);
        Assert.False(saved.Active);
        Assert.True(await store.SetActiveAsync(2, none));
        Assert.False(await store.SetActiveAsync(999, none));
        var active = await store.GetActiveAsync(none);
        Assert.Equal("Database exercise", active!.Definition.Name);
        Assert.Equal(2, active.Version);
        Assert.False((await store.GetAsync(1, none))!.Active);
        Assert.Single(await store.ListAsync(none), v => v.Active);
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT count(*) FROM scada.grid_definitions WHERE active"));
        Assert.Equal("jsonb", await database.ScalarAsync<string>("SELECT data_type FROM information_schema.columns WHERE table_schema='scada' AND table_name='grid_definitions' AND column_name='definition'"));
    }

    [Fact]
    public async Task ConcurrentStoresAllocateUniqueVersionsAndSerializeActivation()
    {
        var database = await postgres.CreateDatabaseAsync();
        var factory = await database.MigratedAsync();
        var stores = Enumerable.Range(0, 5).Select(_ => new PostgresGridDefinitionStore(factory, TimeProvider.System)).ToArray();
        var seeds = await Task.WhenAll(stores.Select(s => s.GetActiveAsync(CancellationToken.None)));
        Assert.All(seeds, s => Assert.Equal(1, s!.Version));
        var versions = await Task.WhenAll(stores.Select(s => s.SaveAsync(DefaultGridDefinition.Create(), "instructor", "parallel", CancellationToken.None)));
        Assert.Equal(Enumerable.Range(2, 5), versions.Select(v => v.Version).Order());
        await Task.WhenAll(stores.Select((s, i) => s.SetActiveAsync(versions[i].Version, CancellationToken.None)));
        Assert.Single(await stores[0].ListAsync(CancellationToken.None), v => v.Active);
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT count(*) FROM pg_indexes WHERE schemaname='scada' AND tablename='grid_definitions' AND indexdef LIKE '%UNIQUE%WHERE%active%'"));
    }
}
