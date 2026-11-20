using Application;
using Npgsql;
using NpgsqlTypes;

namespace Infrastructure;

public sealed class PostgresIncidentStore(NpgsqlDataSource source) : IIncidentStore
{
    public IReadOnlyList<Incident> GetAll()
    {
        using var connection = source.OpenConnection();
        using var command = new NpgsqlCommand(
            "SELECT id, created_at, category, description, actor, status, updated_by, updated_at FROM incidents ORDER BY created_at DESC LIMIT 1000",
            connection);
        using var reader = command.ExecuteReader();
        var incidents = new List<Incident>();
        while (reader.Read())
            incidents.Add(new Incident(reader.GetGuid(0), reader.GetFieldValue<DateTimeOffset>(1),
                reader.GetString(2), reader.GetString(3), reader.GetString(4),
                (IncidentStatus)reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7)));
        return incidents;
    }

    public bool TryAdd(Incident incident)
    {
        using var connection = source.OpenConnection();
        using var command = new NpgsqlCommand(
            "INSERT INTO incidents (id, created_at, category, description, actor, status, updated_by, updated_at) VALUES (@id, @created, @category, @description, @actor, @status, @updatedBy, @updatedAt) ON CONFLICT (id) DO NOTHING",
            connection);
        Bind(command, incident);
        return command.ExecuteNonQuery() == 1;
    }

    public bool TryUpdate(Incident incident, IncidentStatus expectedStatus)
    {
        using var connection = source.OpenConnection();
        using var command = new NpgsqlCommand(
            "UPDATE incidents SET status=@status, updated_by=@updatedBy, updated_at=@updatedAt WHERE id=@id AND status=@expected",
            connection);
        command.Parameters.AddWithValue("id", incident.Id);
        command.Parameters.AddWithValue("status", (int)incident.Status);
        command.Parameters.AddWithValue("expected", (int)expectedStatus);
        command.Parameters.Add("updatedBy", NpgsqlDbType.Text).Value = (object?)incident.UpdatedBy ?? DBNull.Value;
        command.Parameters.Add("updatedAt", NpgsqlDbType.TimestampTz).Value = (object?)incident.UpdatedAt ?? DBNull.Value;
        return command.ExecuteNonQuery() == 1;
    }

    private static void Bind(NpgsqlCommand command, Incident incident)
    {
        command.Parameters.AddWithValue("id", incident.Id);
        command.Parameters.AddWithValue("created", incident.CreatedAt);
        command.Parameters.AddWithValue("category", incident.Category);
        command.Parameters.AddWithValue("description", incident.Description);
        command.Parameters.AddWithValue("actor", incident.Actor);
        command.Parameters.AddWithValue("status", (int)incident.Status);
        command.Parameters.Add("updatedBy", NpgsqlDbType.Text).Value = (object?)incident.UpdatedBy ?? DBNull.Value;
        command.Parameters.Add("updatedAt", NpgsqlDbType.TimestampTz).Value = (object?)incident.UpdatedAt ?? DBNull.Value;
    }
}
