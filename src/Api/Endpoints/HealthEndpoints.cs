namespace Api.Endpoints;

public static class HealthEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/health", async (IServiceProvider services, CancellationToken cancellationToken) =>
        {
            var dataSource = services.GetService<Npgsql.NpgsqlDataSource>();
            if (dataSource is null)
                return Results.Ok(new { status = "healthy", storage = "memory" });
        
            try
            {
                await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
                return Results.Ok(new { status = "healthy", storage = "postgres" });
            }
            catch (Npgsql.NpgsqlException)
            {
                return Results.Json(new { status = "degraded", storage = "postgres" }, statusCode: 503);
            }
        });
    }
}
