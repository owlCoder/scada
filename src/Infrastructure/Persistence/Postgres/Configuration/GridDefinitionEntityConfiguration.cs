using Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Postgres.Configuration;

public static class GridDefinitionEntityConfiguration
{
    public static ModelBuilder Apply(ModelBuilder model)
    {
        model.Entity<GridDefinitionEntity>(e =>
        {
            e.ToTable("grid_definitions");
            e.HasKey(x => x.Version);
            e.Property(x => x.Version).ValueGeneratedNever();
            e.Property(x => x.Definition).HasColumnType("jsonb");
            e.Property(x => x.CreatedBy).HasMaxLength(128);
            e.Property(x => x.Comment).HasMaxLength(4096);
            e.HasIndex(x => x.Active).IsUnique().HasFilter("active = TRUE");
        });
        return model;
    }
}
