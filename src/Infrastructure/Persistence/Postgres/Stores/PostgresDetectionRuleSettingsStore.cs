using Application.Detection;
using Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Postgres.Stores;

public sealed class PostgresDetectionRuleSettingsStore(IDbContextFactory<ScadaDbContext> factory) : IDetectionRuleSettingsStore
{
    private const int MaxAttempts = 3;

    public async Task<IReadOnlyList<DetectionRuleSettings>> LoadAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.DetectionRuleSettings.AsNoTracking().OrderBy(s => s.RuleId).ToListAsync(cancellationToken);
        return rows.Select(r => r.ToRecord()).ToArray();
    }

    public async Task<bool> SaveAsync(DetectionRuleSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var row = await db.DetectionRuleSettings.FirstOrDefaultAsync(s => s.RuleId == settings.RuleId, cancellationToken);
            if (row is null)
                db.DetectionRuleSettings.Add(new DetectionRuleSettingsEntity().Apply(settings));
            else
                row.Apply(settings);

            if (await StoreWrites.SaveIgnoringDuplicateAsync(db, cancellationToken))
                return true;
        }

        return false;
    }
}
