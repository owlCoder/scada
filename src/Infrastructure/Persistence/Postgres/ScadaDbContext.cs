using Infrastructure.Persistence.Postgres.Configuration;
using Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Postgres;

/// <summary>
/// All tables live in the <c>scada</c> schema, so the legacy tables in <c>public</c> (telemetry_samples, security_events,
/// incidents) are never touched by EF; the initial migration only copies their rows.
/// </summary>
public sealed class ScadaDbContext(DbContextOptions<ScadaDbContext> options) : DbContext(options)
{
    public const string Schema = "scada";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public DbSet<GridDefinitionEntity> GridDefinitions => Set<GridDefinitionEntity>();
    public DbSet<EventEntity> Events => Set<EventEntity>();
    public DbSet<TelemetrySampleEntity> Telemetry => Set<TelemetrySampleEntity>();
    public DbSet<AlarmHistoryEntity> AlarmHistory => Set<AlarmHistoryEntity>();
    public DbSet<PlantStateEntity> PlantState => Set<PlantStateEntity>();
    public DbSet<IncidentEntity> Incidents => Set<IncidentEntity>();
    public DbSet<IncidentTimelineEntity> IncidentTimeline => Set<IncidentTimelineEntity>();
    public DbSet<IncidentCommentEntity> IncidentComments => Set<IncidentCommentEntity>();
    public DbSet<DetectionEntity> Detections => Set<DetectionEntity>();
    public DbSet<DetectionRuleSettingsEntity> DetectionRuleSettings => Set<DetectionRuleSettingsEntity>();
    public DbSet<ScenarioRunEntity> ScenarioRuns => Set<ScenarioRunEntity>();
    public DbSet<UserAccountEntity> UserAccounts => Set<UserAccountEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        GridDefinitionEntityConfiguration.Apply(modelBuilder);
        EventEntityConfiguration.Apply(modelBuilder);
        TelemetrySampleEntityConfiguration.Apply(modelBuilder);
        AlarmHistoryEntityConfiguration.Apply(modelBuilder);
        PlantStateEntityConfiguration.Apply(modelBuilder);
        IncidentEntityConfiguration.Apply(modelBuilder);
        IncidentTimelineEntityConfiguration.Apply(modelBuilder);
        IncidentCommentEntityConfiguration.Apply(modelBuilder);
        DetectionEntityConfiguration.Apply(modelBuilder);
        DetectionRuleSettingsEntityConfiguration.Apply(modelBuilder);
        ScenarioRunEntityConfiguration.Apply(modelBuilder);
        UserAccountEntityConfiguration.Apply(modelBuilder);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
                property.SetColumnName(SnakeCaseNames.From(property.Name));
        }
    }

    public static TBuilder Configure<TBuilder>(TBuilder builder, string connectionString) where TBuilder : DbContextOptionsBuilder
    {
        builder.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, Schema));
        return builder;
    }
}
