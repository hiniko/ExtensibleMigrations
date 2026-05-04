using EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Harness;
using Xunit;

namespace EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Scenarios;

[Collection(nameof(PostgresCollection))]
public class MultiMigrationChainTests(PostgresFixture pg)
{
    /// <summary>
    /// Init → modify model in-place → second migration → apply → re-scaffold.
    /// Verifies the snapshot persists between migrations and the differ correctly
    /// detects only the post-init change (not the whole model).
    /// </summary>
    [Fact]
    public async Task Two_migrations_apply_cleanly_and_third_scaffold_is_empty()
    {
        using var fixture = FixtureProject.Copy("Scenario.MaterializedView");
        var ef = new DotnetEfRunner(fixture.ProjectDir);
        var conn = await pg.CreateDatabaseAsync();

        (await ef.AddMigrationAsync("Init", conn)).EnsureSuccess();
        (await ef.UpdateDatabaseAsync(conn)).EnsureSuccess();

        // Mutate the model: add a new entity. Triggers an EF default
        // CreateTable in the second migration without touching the mat view.
        await AddTagEntityAsync(fixture.ProjectDir);

        (await ef.AddMigrationAsync("AddTag", conn)).EnsureSuccess();
        (await ef.UpdateDatabaseAsync(conn)).EnsureSuccess();

        // Second migration should contain CreateTable for Tags but NOT a
        // CREATE MATERIALIZED VIEW (the view was created in Init and lives in
        // the snapshot — re-emitting it would be a regression).
        var migrationFiles = fixture
            .ListMigrationFiles()
            .Where(f =>
                MigrationGoldenFile.IsGeneratedMigrationFile(f)
                && !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
            )
            .OrderBy(f => f)
            .ToList();
        Assert.Equal(2, migrationFiles.Count);

        var addTagContent = await File.ReadAllTextAsync(migrationFiles[1]);
        Assert.Contains("CreateTable", addTagContent);
        Assert.Contains("Tags", addTagContent);
        Assert.DoesNotContain("CREATE MATERIALIZED VIEW", addTagContent);

        // Empty third scaffold confirms the snapshot is in sync.
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

    private static async Task AddTagEntityAsync(string projectDir)
    {
        var domainPath = Path.Combine(projectDir, "Domain.cs");
        var domain = await File.ReadAllTextAsync(domainPath);
        // Add a new DbSet on the context.
        domain = domain.Replace(
            "public DbSet<Order> Orders => Set<Order>();",
            "public DbSet<Order> Orders => Set<Order>();\n    public DbSet<Tag> Tags => Set<Tag>();"
        );
        // Append the Tag entity at the end of the file.
        domain +=
            "\npublic sealed class Tag\n{\n    public int Id { get; set; }\n    public string Name { get; set; } = \"\";\n}\n";
        await File.WriteAllTextAsync(domainPath, domain);
    }
}
