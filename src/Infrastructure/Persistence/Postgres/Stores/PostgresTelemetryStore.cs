using Application.Persistence;
using Application.Telemetry;
using Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Postgres.Stores;

public sealed class PostgresTelemetryStore(IDbContextFactory<ScadaDbContext> factory) : ITelemetryStore, IRetentionTarget
{
    private const int MaxLimit = 10_000;

    public string Name => "telemetry";

    public async Task<int> AppendAsync(IReadOnlyList<TelemetrySample> samples, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
            return 0;

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.Telemetry.AddRange(samples.Select(TelemetrySampleEntity.From));
        await db.SaveChangesAsync(cancellationToken);
        return samples.Count;
    }

    /// <summary>Returns the newest matching samples in chronological order.</summary>
    public async Task<IReadOnlyList<TelemetrySample>> QueryAsync(TelemetryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > MaxLimit)
            throw new ArgumentOutOfRangeException(nameof(query), $"Limit must be between 1 and {MaxLimit}.");

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var items = db.Telemetry.AsNoTracking();
        if (query.TagId is { } tag)
            items = items.Where(s => s.TagId == tag);
        if (query.From is { } from)
            items = items.Where(s => s.Timestamp >= Timestamps.Utc(from));
        if (query.To is { } to)
            items = items.Where(s => s.Timestamp <= Timestamps.Utc(to));

        var newest = await items.OrderByDescending(s => s.Timestamp).ThenByDescending(s => s.Id)
            .Take(query.Limit).ToListAsync(cancellationToken);
        newest.Reverse();
        return newest.Select(s => s.ToRecord()).ToArray();
    }

    public async Task<int> PurgeAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var cutoff = Timestamps.Utc(olderThan);
        return await db.Telemetry.Where(s => s.Timestamp < cutoff).ExecuteDeleteAsync(cancellationToken);
    }
}
