using Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Postgres.Configuration;

public static class IncidentEntityConfiguration
{
    public const string NumberSequence = "incident_number_seq";

    public static ModelBuilder Apply(ModelBuilder model)
    {
        model.HasSequence<long>(NumberSequence);
        model.Entity<IncidentEntity>(e =>
        {
            e.ToTable("incidents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Number).HasDefaultValueSql($"nextval('{ScadaDbContext.Schema}.{NumberSequence}')").ValueGeneratedOnAdd();
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CorrelationKey);
            e.HasIndex(x => x.ScenarioRunId);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Title).HasMaxLength(256);
            e.Property(x => x.Source).HasMaxLength(128);
            e.Property(x => x.AssignedTo).HasMaxLength(128);
            e.Property(x => x.AcknowledgedBy).HasMaxLength(128);
            e.Property(x => x.ResolvedBy).HasMaxLength(128);
            e.Property(x => x.CorrelationKey).HasMaxLength(256);
            e.Property(x => x.Evidence).HasColumnType("jsonb");
            e.HasMany<IncidentTimelineEntity>().WithOne().HasForeignKey(t => t.IncidentId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany<IncidentCommentEntity>().WithOne().HasForeignKey(c => c.IncidentId).OnDelete(DeleteBehavior.Cascade);
        });
        return model;
    }
}
