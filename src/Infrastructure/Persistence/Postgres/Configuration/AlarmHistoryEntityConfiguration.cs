using Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Postgres.Configuration;

public static class AlarmHistoryEntityConfiguration
{
    public static ModelBuilder Apply(ModelBuilder model)
    {
        model.Entity<AlarmHistoryEntity>(e =>
        {
            e.ToTable("alarm_history");
            e.HasKey(x => x.Position);
            e.Property(x => x.Position).UseIdentityAlwaysColumn();
            e.HasIndex(x => x.Id).IsUnique();
            e.HasIndex(x => x.Timestamp);
            e.Property(x => x.AlarmKey).HasMaxLength(128);
            e.Property(x => x.SourceId).HasMaxLength(64);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Transition).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Actor).HasMaxLength(128);
        });
        return model;
    }
}
