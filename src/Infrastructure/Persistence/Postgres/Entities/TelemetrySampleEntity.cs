using Application.Telemetry;

namespace Infrastructure.Persistence.Postgres.Entities;

public sealed class TelemetrySampleEntity
{
    public long Id { get; set; }
    public Guid SampleId { get; set; }
    public long Sequence { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public TimeSpan SimulationTime { get; set; }
    public string TagId { get; set; } = "";
    public double Value { get; set; }
    public string Quality { get; set; } = "";
    public string Hash { get; set; } = "";

    public static TelemetrySampleEntity From(TelemetrySample s) => new()
    {
        SampleId = s.SampleId, Sequence = s.Sequence, Timestamp = Timestamps.Utc(s.Timestamp), SimulationTime = s.SimulationTime, TagId = s.TagId,
        Value = s.Value, Quality = s.Quality, Hash = s.Hash
    };

    public TelemetrySample ToRecord() => new(Sequence, Timestamp, SimulationTime, TagId, Value, Quality, Hash) { SampleId = SampleId };
}
