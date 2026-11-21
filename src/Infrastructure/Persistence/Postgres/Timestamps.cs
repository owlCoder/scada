namespace Infrastructure.Persistence.Postgres;

/// <summary>PostgreSQL <c>timestamptz</c> only accepts UTC values through Npgsql.</summary>
public static class Timestamps
{
    public static DateTimeOffset Utc(DateTimeOffset value) => value.ToUniversalTime();

    public static DateTimeOffset? Utc(DateTimeOffset? value) => value?.ToUniversalTime();
}
