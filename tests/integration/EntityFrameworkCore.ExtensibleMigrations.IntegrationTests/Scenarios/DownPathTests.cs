using EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Harness;
using Npgsql;
using Xunit;

namespace EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Scenarios;

[Collection(nameof(PostgresCollection))]
public class DownPathTests(PostgresFixture pg)
{
    /// <summary>
    /// Apply Init+AddTag, then roll back to Init via <c>dotnet ef database update Init</c>.
    /// Verifies the AddTag Down() actually executed: the Tags table is removed.
    /// </summary>
    [Fact]
    public async Task Rollback_to_previous_migration_removes_added_table()
    {
        using var fixture = FixtureProject.Copy("Scenario.MaterializedView");
        var ef = new DotnetEfRunner(fixture.ProjectDir);
        var conn = await pg.CreateDatabaseAsync();

        (await ef.AddMigrationAsync("Init", conn)).EnsureSuccess();
        (await ef.UpdateDatabaseAsync(conn)).EnsureSuccess();

        await AddTagEntityAsync(fixture.ProjectDir);
        (await ef.AddMigrationAsync("AddTag", conn)).EnsureSuccess();
        (await ef.UpdateDatabaseAsync(conn)).EnsureSuccess();

        Assert.True(await TableExistsAsync(conn, "Tags"), "Tags should exist before rollback");

        (await ef.UpdateDatabaseAsync("Init", conn)).EnsureSuccess();

        Assert.False(
            await TableExistsAsync(conn, "Tags"),
            "Tags should be removed after rollback to Init"
        );
        Assert.True(
            await TableExistsAsync(conn, "Orders"),
            "Orders should still exist (Init applied)"
        );
    }

    /// <summary>
    /// Apply Init, then roll back to base via <c>dotnet ef database update 0</c>.
    /// Verifies the BeforeCore-phase materialized view drop runs in Down.
    /// </summary>
    [Fact]
    public async Task Rollback_to_base_drops_materialized_view()
    {
        using var fixture = FixtureProject.Copy("Scenario.MaterializedView");
        var ef = new DotnetEfRunner(fixture.ProjectDir);
        var conn = await pg.CreateDatabaseAsync();

        (await ef.AddMigrationAsync("Init", conn)).EnsureSuccess();
        (await ef.UpdateDatabaseAsync(conn)).EnsureSuccess();

        Assert.True(await MaterializedViewExistsAsync(conn, "OrderTotalsByCustomer"));

        (await ef.UpdateDatabaseAsync("0", conn)).EnsureSuccess();

        Assert.False(
            await MaterializedViewExistsAsync(conn, "OrderTotalsByCustomer"),
            "Materialized view should be dropped on rollback"
        );
        Assert.False(
            await TableExistsAsync(conn, "Orders"),
            "Orders table should also be dropped on rollback"
        );
    }

    private static async Task<bool> TableExistsAsync(string connectionString, string tableName)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
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
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT 1 FROM pg_matviews WHERE schemaname = 'public' AND matviewname = @n";
        cmd.Parameters.AddWithValue("n", viewName);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    private static async Task AddTagEntityAsync(string projectDir)
    {
        var domainPath = Path.Combine(projectDir, "Domain.cs");
        var domain = await File.ReadAllTextAsync(domainPath);
        domain = domain.Replace(
            "public DbSet<Order> Orders => Set<Order>();",
            "public DbSet<Order> Orders => Set<Order>();\n    public DbSet<Tag> Tags => Set<Tag>();"
        );
        domain +=
            "\npublic sealed class Tag\n{\n    public int Id { get; set; }\n    public string Name { get; set; } = \"\";\n}\n";
        await File.WriteAllTextAsync(domainPath, domain);
    }
}
