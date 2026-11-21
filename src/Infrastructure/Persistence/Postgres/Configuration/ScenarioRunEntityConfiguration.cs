using Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Postgres.Configuration;

public static class ScenarioRunEntityConfiguration
{
    public static ModelBuilder Apply(ModelBuilder model)
    {
        model.Entity<ScenarioRunEntity>(e =>
        {
            e.ToTable("scenario_runs");
            e.HasKey(x => x.RunId);
            e.Property(x => x.RunId).ValueGeneratedNever();
            e.HasIndex(x => x.StartedAt);
            e.Property(x => x.ScenarioId).HasMaxLength(64);
            e.Property(x => x.ScenarioName).HasMaxLength(128);
            e.Property(x => x.Category).HasMaxLength(64);
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.StartedBy).HasMaxLength(128);
            e.Property(x => x.Actions).HasColumnType("jsonb");
            e.Property(x => x.Measurements).HasColumnType("jsonb");
            e.Property(x => x.ExpectedDetections).HasColumnType("jsonb");
            e.Property(x => x.ObservedDetections).HasColumnType("jsonb");
        });
        return model;
    }
}
