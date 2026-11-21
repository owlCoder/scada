using Domain.Incidents;

namespace Infrastructure.Persistence.Postgres.Entities;

public sealed class IncidentCommentEntity
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public string Author { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }

    public static IncidentCommentEntity From(IncidentComment c) => new()
    {
        Id = c.Id,
        IncidentId = c.IncidentId,
        Author = c.Author,
        Text = c.Text,
        CreatedAt = Timestamps.Utc(c.CreatedAt)
    };

    public IncidentComment ToRecord() => new(Id, IncidentId, Author, Text, CreatedAt);
}
