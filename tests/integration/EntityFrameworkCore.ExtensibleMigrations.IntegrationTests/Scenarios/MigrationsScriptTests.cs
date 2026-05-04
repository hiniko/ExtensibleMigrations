using EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Harness;
using Npgsql;
using Xunit;

namespace EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Scenarios;

[Collection(nameof(PostgresCollection))]
public class MigrationsScriptTests(PostgresFixture pg)
{
    /// <summary>
    /// `dotnet ef migrations script --idempotent` should produce SQL that
    /// includes custom-op output and runs cleanly against a fresh database
    /// to the same end state as <c>ef database update</c>.
    /// </summary>
    [Fact]
    public async Task Idempotent_script_includes_custom_ops_and_applies_cleanly()
    {
        using var fixture = FixtureProject.Copy("Scenario.MaterializedView");
        var ef = new DotnetEfRunner(fixture.ProjectDir);

        (await ef.AddMigrationAsync("Init")).EnsureSuccess();

        var scriptPath = Path.Combine(Path.GetTempPath(), $"em-script-{Guid.NewGuid():N}.sql");
        try
        {
            var conn = await pg.CreateDatabaseAsync();
            (await ef.GenerateScriptAsync(conn, scriptPath, idempotent: true)).EnsureSuccess();

            var sql = await File.ReadAllTextAsync(scriptPath);
            Assert.Contains("CREATE MATERIALIZED VIEW", sql);
            Assert.Contains("__EFMigrationsHistory", sql);

            await ExecuteSqlAsync(conn, sql);

            Assert.True(await TableExistsAsync(conn, "Orders"));
            Assert.True(await MaterializedViewExistsAsync(conn, "OrderTotalsByCustomer"));

            // Re-running the script must be a no-op (idempotent guard).
            await ExecuteSqlAsync(conn, sql);
            Assert.True(await TableExistsAsync(conn, "Orders"));
            Assert.True(await MaterializedViewExistsAsync(conn, "OrderTotalsByCustomer"));
        }
        finally
        {
            if (File.Exists(scriptPath))
                File.Delete(scriptPath);
        }
    }

    private static async Task ExecuteSqlAsync(string connectionString, string sql)
    {
        await using var c = new NpgsqlConnection(connectionString);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<bool> TableExistsAsync(string connectionString, string tableName)
    {
        await using var c = new NpgsqlConnection(connectionString);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText =
            "SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = @n";
        cmd.Parameters.AddWithValue("n", tableName);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    private static async Task<bool> MaterializedViewExistsAsync(
        string connectionString,
        string viewName
    )
    {
        await using var c = new NpgsqlConnection(connectionString);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText =
            "SELECT 1 FROM pg_matviews WHERE schemaname = 'public' AND matviewname = @n";
        cmd.Parameters.AddWithValue("n", viewName);
        return await cmd.ExecuteScalarAsync() is not null;
    }
}
