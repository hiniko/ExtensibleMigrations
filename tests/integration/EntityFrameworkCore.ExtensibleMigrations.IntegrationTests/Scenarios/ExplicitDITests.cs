using EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Harness;
using Xunit;

namespace EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Scenarios;

public class ExplicitDITests
{
    /// <summary>
    /// Fixture handlers carry no <c>[CustomMigrationHandler]</c> attribute.
    /// Registration goes through the public DI extensions:
    /// <c>AddMigrationOperationHandler&lt;T&gt;()</c> / <c>AddCSharpMigrationOperationHandler&lt;T&gt;()</c>.
    /// Verifies the explicit-registration path produces the same scaffold output
    /// as attribute-based discovery.
    /// </summary>
    [Fact]
    public async Task Explicit_DI_registration_drives_handler_emission()
    {
        using var fixture = FixtureProject.Copy("Scenario.ExplicitDI");
        var ef = new DotnetEfRunner(fixture.ProjectDir);

        (await ef.AddMigrationAsync("Init")).EnsureSuccess();

        var migrationFile = fixture
            .ListMigrationFiles()
            .Single(f =>
                MigrationGoldenFile.IsGeneratedMigrationFile(f)
                && !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
            );
        var migration = await File.ReadAllTextAsync(migrationFile);

        Assert.Contains("CreateTable", migration);
        Assert.Contains(
            "// TaggedMarker: Scenario.ExplicitDI.Article -> explicit-di-trigger",
            migration
        );
    }
}
