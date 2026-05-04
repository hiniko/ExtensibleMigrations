using EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Harness;
using Xunit;

namespace EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Scenarios;

[Collection(nameof(PostgresCollection))]
public class NativeIndexFromAttributeTests(PostgresFixture pg)
{
    /// <summary>
    /// Counterpoint to <c>Scenario.AttributeDriven</c>: the same property-level
    /// attribute drives a native EF Core index via Npgsql provider extensions
    /// rather than a custom MigrationOperation. Verifies:
    ///   1. The migration emits EF's standard <c>migrationBuilder.CreateIndex</c>
    ///      with method/operators (no Sql() call).
    ///   2. The snapshot records the index as a typed <c>HasIndex</c> entry,
    ///      not just a property annotation.
    /// </summary>
    [Fact]
    public async Task Native_EF_index_lands_in_migration_and_snapshot()
    {
        using var fixture = FixtureProject.Copy("Scenario.NativeIndexFromAttribute");
        var ef = new DotnetEfRunner(fixture.ProjectDir);

        (await ef.AddMigrationAsync("Init")).EnsureSuccess();

        var migrationFile = fixture
            .ListMigrationFiles()
            .Single(f =>
                MigrationGoldenFile.IsGeneratedMigrationFile(f)
                && !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
            );
        var migration = await File.ReadAllTextAsync(migrationFile);

        // EF's default index emitter — not a Sql() call, a typed CreateIndex.
        Assert.Contains("migrationBuilder.CreateIndex(", migration);
        Assert.Contains("ix_ft_Articles_Title", migration);
        Assert.Contains("ix_ft_Articles_Body", migration);
        Assert.Contains("\"gin\"", migration);
        Assert.Contains("gin_trgm_ops", migration);
        Assert.DoesNotContain("ix_ft_Articles_Author", migration);

        var snapshot = await File.ReadAllTextAsync(
            Path.Combine(fixture.ProjectDir, "Migrations", "ArticleContextModelSnapshot.cs")
        );

        // Index appears in the snapshot as a real HasIndex entry — that's the
        // whole point of this fixture vs Scenario.AttributeDriven.
        Assert.Contains("HasIndex", snapshot);
        Assert.Contains("Title", snapshot);
        Assert.Contains("Body", snapshot);
        Assert.Contains("HasMethod", snapshot);
        Assert.Contains("gin_trgm_ops", snapshot);

        // Postgres extension also lands as a typed snapshot entry (Npgsql native API).
        Assert.Contains("HasPostgresExtension", snapshot);
        Assert.Contains("pg_trgm", snapshot);
    }

    [Fact]
    public async Task Apply_and_roundtrip_against_postgres()
    {
        using var fixture = FixtureProject.Copy("Scenario.NativeIndexFromAttribute");
        var ef = new DotnetEfRunner(fixture.ProjectDir);
        var conn = await pg.CreateDatabaseAsync();

        (await ef.AddMigrationAsync("Init", conn)).EnsureSuccess();
        (await ef.UpdateDatabaseAsync(conn)).EnsureSuccess();

        (await ef.AddMigrationAsync("Empty", conn)).EnsureSuccess();
        var emptyMigrationFile = fixture
            .ListMigrationFiles()
            .Where(f =>
                MigrationGoldenFile.IsGeneratedMigrationFile(f)
                && !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
            )
            .OrderBy(f => f)
            .Last();
        MigrationGoldenFile.AssertEmptyMigrationBody(
            await File.ReadAllTextAsync(emptyMigrationFile)
        );
    }
}
