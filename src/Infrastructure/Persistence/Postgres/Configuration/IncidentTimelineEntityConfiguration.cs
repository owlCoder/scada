using Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Postgres.Configuration;

public static class IncidentTimelineEntityConfiguration
{
    public static ModelBuilder Apply(ModelBuilder model)
    {
        model.Entity<IncidentTimelineEntity>(e =>
        {
            e.ToTable("incident_timeline");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => new { x.IncidentId, x.Timestamp });
            e.Property(x => x.Actor).HasMaxLength(128);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(16);
        });
        return model;
    }
}
