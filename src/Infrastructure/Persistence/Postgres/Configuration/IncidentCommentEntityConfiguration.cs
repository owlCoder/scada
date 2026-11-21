using Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Postgres.Configuration;

public static class IncidentCommentEntityConfiguration
{
    public static ModelBuilder Apply(ModelBuilder model)
    {
        model.Entity<IncidentCommentEntity>(e =>
        {
            e.ToTable("incident_comments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => new { x.IncidentId, x.CreatedAt });
            e.Property(x => x.Author).HasMaxLength(128);
        });
        return model;
    }
}
