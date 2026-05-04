using EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Harness;
using Xunit;

namespace EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Scenarios;

[Collection(nameof(PostgresCollection))]
public class MultiExtensionTests(PostgresFixture pg)
{
    [Fact]
    public async Task Two_handlers_emit_in_attribute_order_and_match_golden()
    {
        using var fixture = FixtureProject.Copy("Scenario.MultiExtension");
        var ef = new DotnetEfRunner(fixture.ProjectDir);

        (await ef.AddMigrationAsync("Init")).EnsureSuccess();

        var migrationFile = fixture
            .ListMigrationFiles()
            .Single(f =>
                MigrationGoldenFile.IsGeneratedMigrationFile(f) && !f.EndsWith(".Designer.cs")
            );
        var migrationContent = await File.ReadAllTextAsync(migrationFile);

        // MatViewHandler is Order=200, GinIndexHandler is Order=300.
        // Both emit in AfterCore phase; lower Order should appear first in the migration body.
        var matViewIdx = migrationContent.IndexOf(
            "CREATE MATERIALIZED VIEW",
            StringComparison.Ordinal
        );
        var ginIdx = migrationContent.IndexOf("CREATE INDEX", StringComparison.Ordinal);
        Assert.True(matViewIdx > 0 && ginIdx > 0, "Both ops should be emitted");
        Assert.True(
            matViewIdx < ginIdx,
            $"MatView (Order=200) must precede GIN index (Order=300). matView@{matViewIdx} gin@{ginIdx}"
        );

        // Golden compare.
        var goldenDir = Path.Combine(GoldenRoot(), "Scenario.MultiExtension");
        MigrationGoldenFile.AssertMatches(
            migrationContent,
            Path.Combine(goldenDir, "Init.expected.cs")
        );
    }

    [Fact]
    public async Task Apply_and_roundtrip_against_postgres()
    {
        using var fixture = FixtureProject.Copy("Scenario.MultiExtension");
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
            dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir!, "tests", "integration", "golden");
    }
}
