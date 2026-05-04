using EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Harness;
using Xunit;

namespace EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Scenarios;

[Collection(nameof(PostgresCollection))]
public class MaterializedViewTests(PostgresFixture pg)
{
    /// <summary>
    /// Scaffold-only: tests that the migration .cs and snapshot .cs match the
    /// committed golden files. Does not require Docker.
    /// </summary>
    [Fact]
    public async Task Scaffold_matches_golden()
    {
        using var fixture = FixtureProject.Copy("Scenario.MaterializedView");
        var ef = new DotnetEfRunner(fixture.ProjectDir);

        // Scaffold without a real DB connection — design-time only.
        (await ef.AddMigrationAsync("Init")).EnsureSuccess();

        var migrationFile = fixture
            .ListMigrationFiles()
            .Single(f =>
                MigrationGoldenFile.IsGeneratedMigrationFile(f)
                && !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
            );
        var snapshotFile = Path.Combine(
            fixture.ProjectDir,
            "Migrations",
            "OrderContextModelSnapshot.cs"
        );

        var goldenDir = Path.Combine(GoldenRoot(), "Scenario.MaterializedView");
        var migrationContent = await File.ReadAllTextAsync(migrationFile);
        var snapshotContent = await File.ReadAllTextAsync(snapshotFile);

        MigrationGoldenFile.AssertMatches(
            migrationContent,
            Path.Combine(goldenDir, "Init.expected.cs")
        );
        MigrationGoldenFile.AssertMatches(
            snapshotContent,
            Path.Combine(goldenDir, "OrderContextModelSnapshot.expected.cs")
        );

        // Snapshot-shape assertions: the materialised view must appear as a
        // typed keyless+ToView entry, not just as raw HasAnnotation calls.
        // Guards the Tier 1 contract from regressing under future EF versions.
        Assert.Contains("Scenario.MaterializedView.OrderTotal", snapshotContent);
        Assert.Contains("b.ToView(\"OrderTotalsByCustomer\"", snapshotContent);
        Assert.Contains("b.ToTable((string)null)", snapshotContent); // keyless: not table-mapped
        Assert.Contains("HasAnnotation(\"MatView:Query\"", snapshotContent);
    }

    /// <summary>
    /// Roundtrip: scaffold → apply to real Postgres → re-scaffold and assert
    /// the migration body is empty. Requires Docker.
    /// </summary>
    [Fact]
    public async Task Apply_and_roundtrip_against_postgres()
    {
        using var fixture = FixtureProject.Copy("Scenario.MaterializedView");
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

    private static string GoldenRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "ExtensibleMigrations.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return Path.Combine(dir!, "tests", "integration", "golden");
    }
}
