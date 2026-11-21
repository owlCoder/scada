using Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Postgres.Configuration;

public static class DetectionEntityConfiguration
{
    public static ModelBuilder Apply(ModelBuilder model)
    {
        model.Entity<DetectionEntity>(e =>
        {
            e.ToTable("detections");
            e.HasKey(x => x.Position);
            e.Property(x => x.Position).UseIdentityAlwaysColumn();
            e.HasIndex(x => x.Id).IsUnique();
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => x.RuleId);
            e.HasIndex(x => x.ScenarioRunId);
            e.Property(x => x.RuleId).HasMaxLength(64);
            e.Property(x => x.RuleName).HasMaxLength(128);
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Title).HasMaxLength(256);
            e.Property(x => x.DeviceId).HasMaxLength(64);
            e.Property(x => x.Fingerprint).HasMaxLength(256);
            e.Property(x => x.Evidence).HasColumnType("jsonb");
        });
        return model;
    }
}
