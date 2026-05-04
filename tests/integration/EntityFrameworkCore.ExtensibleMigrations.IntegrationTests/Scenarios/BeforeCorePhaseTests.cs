using EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Harness;
using Xunit;

namespace EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Scenarios;

[Collection(nameof(PostgresCollection))]
public class BeforeCorePhaseTests(PostgresFixture pg)
{
    [Fact]
    public async Task Handler_emits_before_core_create_table()
    {
        using var fixture = FixtureProject.Copy("Scenario.BeforeCorePhase");
        var ef = new DotnetEfRunner(fixture.ProjectDir);

        (await ef.AddMigrationAsync("Init")).EnsureSuccess();

        var migrationFile = fixture
            .ListMigrationFiles()
            .Single(f =>
                MigrationGoldenFile.IsGeneratedMigrationFile(f)
                && !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
            );
        var content = await File.ReadAllTextAsync(migrationFile);

        var extIdx = content.IndexOf("CREATE EXTENSION", StringComparison.Ordinal);
        var createTableIdx = content.IndexOf(
            "migrationBuilder.CreateTable",
            StringComparison.Ordinal
        );
        Assert.True(extIdx > 0, "Expected CREATE EXTENSION SQL in migration");
        Assert.True(createTableIdx > 0, "Expected CreateTable in migration");
        Assert.True(
            extIdx < createTableIdx,
            $"BeforeCore handler should emit CREATE EXTENSION before EF's CreateTable. extension@{extIdx} createTable@{createTableIdx}"
        );

        // Down() emits in reverse — DROP EXTENSION must come after DropTable in source order.
        var dropExtIdx = content.IndexOf("DROP EXTENSION", StringComparison.Ordinal);
        var dropTableIdx = content.IndexOf("migrationBuilder.DropTable", StringComparison.Ordinal);
        Assert.True(
            dropExtIdx > 0 && dropTableIdx > 0,
            "Expected both DropTable and DROP EXTENSION in Down"
        );
        Assert.True(
            dropTableIdx < dropExtIdx,
            $"Down() should reverse phase order: DropTable before DROP EXTENSION. dropTable@{dropTableIdx} dropExt@{dropExtIdx}"
        );

        // Snapshot-shape assertion: the canonical Pg:Extensions annotation
        // lives on the model. EF's default writer serialises it. Guards
        // against a future EF version dropping that auto-emission.
        var snapshot = await File.ReadAllTextAsync(
            Path.Combine(fixture.ProjectDir, "Migrations", "DocumentContextModelSnapshot.cs")
        );
        Assert.Contains("\"Pg:Extensions\"", snapshot);
        Assert.Contains("\"unaccent\"", snapshot);
    }

    [Fact]
    public async Task Apply_and_roundtrip_against_postgres()
    {
        using var fixture = FixtureProject.Copy("Scenario.BeforeCorePhase");
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
