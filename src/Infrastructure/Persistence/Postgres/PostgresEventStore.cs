using Application;
using Npgsql;

namespace Infrastructure;

public sealed class PostgresEventStore(NpgsqlDataSource dataSource) : IEventStore
{
    public bool Append(SecurityEvent securityEvent)
    {
        using var connection = dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            "INSERT INTO security_events (occurred_at, category, message, actor) VALUES (@time, @category, @message, @actor)",
            connection);
        command.Parameters.AddWithValue("time", securityEvent.Timestamp);
        command.Parameters.AddWithValue("category", securityEvent.Category);
        command.Parameters.AddWithValue("message", securityEvent.Message);
        command.Parameters.AddWithValue("actor", securityEvent.Actor);
        return command.ExecuteNonQuery() == 1;
    }

    public IReadOnlyList<SecurityEvent> GetRecent()
    {
        using var connection = dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            "SELECT occurred_at, category, message, actor FROM security_events ORDER BY id DESC LIMIT 1000",
            connection);
        using var reader = command.ExecuteReader();
        var events = new List<SecurityEvent>();
        while (reader.Read())
            events.Add(new(reader.GetFieldValue<DateTimeOffset>(0), reader.GetString(1),
                reader.GetString(2), reader.GetString(3)));
        return events;
    }
}
