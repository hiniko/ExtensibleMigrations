using EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Harness;
using Xunit;

namespace EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Scenarios;

public class SnapshotOnlyTests
{
    /// <summary>
    /// Fixture exposes only an <c>IMigrationsSnapshotHandler</c> — no operation
    /// handler. Verifies the <c>ICSharpSnapshotGenerator</c> wrap path runs
    /// independently and the handler's output lands in the snapshot file.
    /// </summary>
    [Fact]
    public async Task Snapshot_handler_runs_without_an_operation_handler()
    {
        using var fixture = FixtureProject.Copy("Scenario.SnapshotOnly");
        var ef = new DotnetEfRunner(fixture.ProjectDir);

        (await ef.AddMigrationAsync("Init")).EnsureSuccess();

        var snapshotPath = Path.Combine(
            fixture.ProjectDir,
            "Migrations",
            "WidgetContextModelSnapshot.cs"
        );
        Assert.True(File.Exists(snapshotPath), $"Expected snapshot at {snapshotPath}");

        var snapshot = await File.ReadAllTextAsync(snapshotPath);
        Assert.Contains("MetaOwner: Scenario.SnapshotOnly.Widget -> team-platform", snapshot);

        // No custom op handler → migration body has only EF default ops.
        var migrationFile = fixture
            .ListMigrationFiles()
            .Single(f =>
                MigrationGoldenFile.IsGeneratedMigrationFile(f)
                && !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
            );
        var migration = await File.ReadAllTextAsync(migrationFile);
        Assert.Contains("CreateTable", migration);
        Assert.DoesNotContain("MetaOwner", migration);
    }
}
