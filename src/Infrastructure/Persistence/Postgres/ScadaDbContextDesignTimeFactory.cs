using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infrastructure.Persistence.Postgres;

/// <summary>Used by <c>dotnet ef</c> to build the model. Generating migrations needs no running database.</summary>
public sealed class ScadaDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ScadaDbContext>
{
    public ScadaDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Scada")
            ?? "Host=localhost;Database=scada;Username=scada";
        return new ScadaDbContext(ScadaDbContext.Configure(new DbContextOptionsBuilder<ScadaDbContext>(), connectionString).Options);
    }
}
