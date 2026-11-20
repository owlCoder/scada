using Application;
using Npgsql;

namespace Infrastructure;

public sealed class PostgresTelemetryStore(NpgsqlDataSource dataSource) : ITelemetryStore
{
    public bool Append(TelemetrySample sample)
    {
        using var connection = dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            "INSERT INTO telemetry_samples (recorded_at, tank_level, pump_running, revision) VALUES (@time, @level, @running, @revision)",
            connection);
        command.Parameters.AddWithValue("time", sample.Timestamp);
        command.Parameters.AddWithValue("level", sample.TankLevel);
        command.Parameters.AddWithValue("running", sample.PumpRunning);
        command.Parameters.AddWithValue("revision", sample.Revision);
        return command.ExecuteNonQuery() == 1;
    }

    public IReadOnlyList<TelemetrySample> GetRecent(int count)
    {
        if (count is < 1 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(count));

        using var connection = dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            "SELECT recorded_at, tank_level, pump_running, revision FROM telemetry_samples ORDER BY id DESC LIMIT @count",
            connection);
        command.Parameters.AddWithValue("count", count);
        using var reader = command.ExecuteReader();
        var samples = new List<TelemetrySample>();
        while (reader.Read())
            samples.Add(new(reader.GetFieldValue<DateTimeOffset>(0), reader.GetDouble(1),
                reader.GetBoolean(2), reader.GetInt64(3)));
        samples.Reverse();
        return samples;
    }
}
