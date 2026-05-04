#pragma warning disable EF1001 // Internal EF Core API usage (SqliteDesignTimeServices)
using EntityFrameworkCore.ExtensibleMigrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Sqlite.Design.Internal;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
#pragma warning restore EF1001

namespace EntityFrameworkCore.ExtensibleMigrations.Tests;

public class EndToEndMigrationScaffoldingTests
{
    private sealed class Widget
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class TestContext : DbContext
    {
        public DbSet<Widget> Widgets => Set<Widget>();

        protected override void OnConfiguring(DbContextOptionsBuilder o) =>
            o.UseSqlite("DataSource=:memory:");
    }

    [Fact]
    public void Scaffolded_migration_includes_custom_op_emit_in_AfterCore_phase()
    {
        using var ctx = new TestContext();

        var designServices = new ServiceCollection();
        designServices.AddEntityFrameworkDesignTimeServices();
        designServices.AddDbContextDesignTimeServices(ctx);
        new SqliteDesignTimeServices().ConfigureDesignTimeServices(designServices);
        new ExtensibleMigrationsDesignTimeServices().ConfigureDesignTimeServices(designServices);

        var sp = designServices.BuildServiceProvider();
        var scaffolder = sp.GetRequiredService<IMigrationsScaffolder>();

        var migration = scaffolder.ScaffoldMigration("Init", "TestNs");

        Assert.Contains("E2E_OP_EMITTED", migration.MigrationCode);
        Assert.Contains("E2E_SNAPSHOT_HANDLER_RAN", migration.SnapshotCode);
        var sqlIdx = migration.MigrationCode.IndexOf("E2E_OP_EMITTED", StringComparison.Ordinal);
        var createTableIdx = migration.MigrationCode.IndexOf(
            "CreateTable",
            StringComparison.Ordinal
        );
        Assert.True(
            createTableIdx > 0 && sqlIdx > createTableIdx,
            "AfterCore op must follow CreateTable in Up()"
        );
    }
}
