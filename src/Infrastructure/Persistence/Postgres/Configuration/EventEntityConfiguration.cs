using Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Postgres.Configuration;

public static class EventEntityConfiguration
{
    public static ModelBuilder Apply(ModelBuilder model)
    {
        model.Entity<EventEntity>(e =>
        {
            e.ToTable("events");
            e.HasKey(x => x.Position);
            e.Property(x => x.Position).UseIdentityAlwaysColumn();
            e.HasIndex(x => x.Id).IsUnique();
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => x.Category);
            e.HasIndex(x => x.ScenarioRunId);
            e.Property(x => x.Category).HasMaxLength(64);
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Source).HasMaxLength(128);
            e.Property(x => x.Actor).HasMaxLength(128);
            e.Property(x => x.DeviceId).HasMaxLength(64);
            e.Property(x => x.CorrelationId).HasMaxLength(128);
        });
        return model;
    }
}
