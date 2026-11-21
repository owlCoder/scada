using Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Postgres.Configuration;

public static class TelemetrySampleEntityConfiguration
{
    public static ModelBuilder Apply(ModelBuilder model)
    {
        model.Entity<TelemetrySampleEntity>(e =>
        {
            e.ToTable("telemetry");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityAlwaysColumn();
            e.HasIndex(x => new { x.TagId, x.Timestamp });
            e.HasIndex(x => x.Timestamp);
            e.Property(x => x.SampleId).HasDefaultValueSql("gen_random_uuid()");
            e.HasIndex(x => x.SampleId).IsUnique();
            e.Property(x => x.TagId).HasMaxLength(64);
            e.Property(x => x.Quality).HasMaxLength(32);
            e.Property(x => x.Hash).HasMaxLength(128);
        });
        return model;
    }
}
