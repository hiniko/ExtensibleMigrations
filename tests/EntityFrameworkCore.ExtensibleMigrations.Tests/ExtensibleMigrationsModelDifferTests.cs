using EntityFrameworkCore.ExtensibleMigrations;
using EntityFrameworkCore.ExtensibleMigrations.Tests.Stubs;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.ExtensibleMigrations.Tests;

public class ExtensibleMigrationsModelDifferTests
{
    [Fact]
    public void HasDifferences_true_when_only_handler_reports_diff()
    {
        var inner = new SpyMigrationsModelDiffer { ReturnHasDifferences = false };
        var sp = new ServiceCollection()
            .AddTransient<IMigrationOperationHandler, HandlerSaysYes>()
            .BuildServiceProvider();
        var sut = new ExtensibleMigrationsModelDiffer(inner, sp);

        Assert.True(sut.HasDifferences(null, null));
    }

    [Fact]
    public void GetDifferences_concatenates_default_then_handler_ops()
    {
        var coreOp = new AddColumnOperation { Name = "X", Table = "T" };
        var inner = new SpyMigrationsModelDiffer { ReturnDifferences = new[] { coreOp } };
        var sp = new ServiceCollection()
            .AddTransient<IMigrationOperationHandler, HandlerEmitsOp>()
            .BuildServiceProvider();
        var sut = new ExtensibleMigrationsModelDiffer(inner, sp);

        var diffs = sut.GetDifferences(null, null);

        // Default operation should be first
        Assert.Same(coreOp, diffs[0]);
        // Should have at least 2 ops (core + our handler)
        Assert.True(diffs.Count >= 2);
        // Our handler's operation should be in the list with our marker
        Assert.Contains(diffs, op => op is SpyOperation spy && spy.Marker == "H");
    }

    [Fact]
    public void GetDifferences_passes_existing_ops_to_subsequent_handlers()
    {
        HandlerOrder200Inspects.SawHandlerOrder100Op = false;
        var inner = new SpyMigrationsModelDiffer();
        var sp = new ServiceCollection()
            .AddTransient<IMigrationOperationHandler, HandlerOrder100Emits>()
            .AddTransient<IMigrationOperationHandler, HandlerOrder200Inspects>()
            .BuildServiceProvider();
        var sut = new ExtensibleMigrationsModelDiffer(inner, sp);

        sut.GetDifferences(null, null);

        Assert.True(HandlerOrder200Inspects.SawHandlerOrder100Op);
    }

    [CustomMigrationHandler(Order = 250)]
    public sealed class HandlerSaysYes : IMigrationOperationHandler
    {
        public bool HasDifferences(IRelationalModel? s, IRelationalModel? t, bool d) => true;

        public IReadOnlyList<MigrationOperation> GetOperations(
            IRelationalModel? s,
            IRelationalModel? t,
            IReadOnlyList<MigrationOperation> e
        ) => Array.Empty<MigrationOperation>();
    }

    [CustomMigrationHandler(Order = 251)]
    public sealed class HandlerEmitsOp : IMigrationOperationHandler
    {
        public bool HasDifferences(IRelationalModel? s, IRelationalModel? t, bool d) => false;

        public IReadOnlyList<MigrationOperation> GetOperations(
            IRelationalModel? s,
            IRelationalModel? t,
            IReadOnlyList<MigrationOperation> e
        ) => new[] { new SpyOperation { Marker = "H" } };
    }

    [CustomMigrationHandler(Order = 252)]
    public sealed class HandlerOrder100Emits : IMigrationOperationHandler
    {
        public bool HasDifferences(IRelationalModel? s, IRelationalModel? t, bool d) => false;

        public IReadOnlyList<MigrationOperation> GetOperations(
            IRelationalModel? s,
            IRelationalModel? t,
            IReadOnlyList<MigrationOperation> e
        ) => new[] { new SpyOperation { Marker = "from-100" } };
    }

    [CustomMigrationHandler(Order = 253)]
    public sealed class HandlerOrder200Inspects : IMigrationOperationHandler
    {
        public static bool SawHandlerOrder100Op;

        public bool HasDifferences(IRelationalModel? s, IRelationalModel? t, bool d) => false;

        public IReadOnlyList<MigrationOperation> GetOperations(
            IRelationalModel? s,
            IRelationalModel? t,
            IReadOnlyList<MigrationOperation> e
        )
        {
            SawHandlerOrder100Op = e.OfType<SpyOperation>().Any(o => o.Marker == "from-100");
            return Array.Empty<MigrationOperation>();
        }
    }
}
