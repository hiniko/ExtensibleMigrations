using Xunit;

namespace EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Harness;

[Collection(nameof(PostgresCollection))]
public class PostgresFixtureSmokeTests
{
    private readonly PostgresFixture _pg;

    public PostgresFixtureSmokeTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Container_starts_and_creates_unique_databases()
    {
        var c1 = await _pg.CreateDatabaseAsync();
        var c2 = await _pg.CreateDatabaseAsync();

        Assert.NotEqual(c1, c2);
        Assert.Contains("Database=em_", c1);
        Assert.Contains("Database=em_", c2);
    }

    [Fact]
    public async Task DotnetEfRunner_reports_failure_on_invalid_args()
    {
        var dir = Path.GetTempPath();
        var runner = new DotnetEfRunner(dir);
        var result = await runner.AddMigrationAsync("X");

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.CombinedOutput);
    }
}
