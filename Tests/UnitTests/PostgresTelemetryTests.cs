using Infrastructure;
using Npgsql;
using Xunit;

namespace UnitTests;

public sealed class PostgresTelemetryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3601)]
    public void InvalidCountIsRejectedBeforeDatabaseAccess(int count)
    {
        using var source = NpgsqlDataSource.Create("Host=127.0.0.1;Database=scada;Username=scada;Password=unused;Timeout=1");
        var store = new PostgresTelemetryStore(source);

        Assert.Throws<ArgumentOutOfRangeException>(() => store.GetRecent(count));
    }
}
