using Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Postgres.Configuration;

public static class PlantStateEntityConfiguration
{
    public static ModelBuilder Apply(ModelBuilder model)
    {
        model.Entity<PlantStateEntity>(e =>
        {
            e.ToTable("plant_state", t => t.HasCheckConstraint("ck_plant_state_singleton", $"id = {PlantStateEntity.SingletonId}"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Plant).HasColumnType("jsonb");
            e.Property(x => x.Controllers).HasColumnType("jsonb");
        });
        return model;
    }
}
