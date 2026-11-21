using Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Postgres.Configuration;

public static class DetectionRuleSettingsEntityConfiguration
{
    public static ModelBuilder Apply(ModelBuilder model)
    {
        model.Entity<DetectionRuleSettingsEntity>(e =>
        {
            e.ToTable("detection_rule_settings");
            e.HasKey(x => x.RuleId);
            e.Property(x => x.RuleId).HasMaxLength(64);
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(16);
        });
        return model;
    }
}
