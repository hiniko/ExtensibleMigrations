using EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Harness;
using Xunit;

namespace EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Scenarios;

[Collection(nameof(PostgresCollection))]
public class AttributeDrivenTests(PostgresFixture pg)
{
    /// <summary>
    /// Property-level <c>[FullText]</c> attribute → ModelBuilder extension →
    /// EF annotation → handler. Verifies the full attribute-driven dev-ex flow
    /// produces an index per attributed property and ignores unattributed ones.
    /// </summary>
    [Fact]
    public async Task Scaffold_emits_one_index_per_attributed_property()
    {
        using var fixture = FixtureProject.Copy("Scenario.AttributeDriven");
        var ef = new DotnetEfRunner(fixture.ProjectDir);

        (await ef.AddMigrationAsync("Init")).EnsureSuccess();

        var migrationFile = fixture
            .ListMigrationFiles()
            .Single(f =>
                MigrationGoldenFile.IsGeneratedMigrationFile(f)
                && !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
            );
        var content = await File.ReadAllTextAsync(migrationFile);

        Assert.Contains("ix_ft_Articles_Title", content);
        Assert.Contains("ix_ft_Articles_Body", content);
        Assert.DoesNotContain("ix_ft_Articles_Author", content);
        Assert.Contains("USING gin (to_tsvector('english',", content);

        // FullText:* annotations land in the snapshot via EF's default writer
        // (no custom snapshot handler needed). Guard the typed-entry contract.
        var snapshot = await File.ReadAllTextAsync(
            Path.Combine(fixture.ProjectDir, "Migrations", "ArticleContextModelSnapshot.cs")
        );
        Assert.Contains("\"FullText:IsFullText\"", snapshot);
        Assert.Contains("\"FullText:Language\"", snapshot);
    }

    [Fact]
    public async Task Apply_and_roundtrip_against_postgres()
    {
        using var fixture = FixtureProject.Copy("Scenario.AttributeDriven");
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
