namespace Infrastructure.Persistence.Postgres.Entities;

public sealed class GridDefinitionEntity
{
    public int Version { get; set; }
    public string Definition { get; set; } = "{}";
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string Comment { get; set; } = "";
    public bool Active { get; set; }
}
