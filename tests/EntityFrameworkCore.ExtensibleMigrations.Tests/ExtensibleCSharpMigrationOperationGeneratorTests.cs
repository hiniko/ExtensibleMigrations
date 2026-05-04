using EntityFrameworkCore.ExtensibleMigrations;
using EntityFrameworkCore.ExtensibleMigrations.Tests.Stubs;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.ExtensibleMigrations.Tests;

public class ExtensibleCSharpMigrationOperationGeneratorTests
{
    [Fact]
    public void Emits_BeforeCore_then_core_then_AfterCore_in_input_order()
    {
        var spy = new SpyCSharpMigrationOperationGenerator();
        var sp = BuildProvider();
        var sut = new ExtensibleCSharpMigrationOperationGenerator(spy, sp);
        var builder = new IndentedStringBuilder();

        var ops = new MigrationOperation[]
        {
            new SpyOperation { Marker = "A_after" },
            new AddColumnOperation { Name = "Col1", Table = "T" },
            new DropSpyOperation { Marker = "B_before" },
        };

        sut.Generate("mb", ops, builder);

        var output = builder.ToString();
        var beforeIdx = output.IndexOf("BEFORE:B_before", StringComparison.Ordinal);
        var coreIdx = output.IndexOf("CORE:AddColumnOperation", StringComparison.Ordinal);
        var afterIdx = output.IndexOf("AFTER:A_after", StringComparison.Ordinal);

        Assert.True(beforeIdx >= 0 && coreIdx >= 0 && afterIdx >= 0, output);
        Assert.True(beforeIdx < coreIdx, "BeforeCore must come before core");
        Assert.True(coreIdx < afterIdx, "AfterCore must come after core");
    }

    [Fact]
    public void No_handlers_routes_all_to_default_generator()
    {
        var spy = new SpyCSharpMigrationOperationGenerator();
        var sp = new ServiceCollection().BuildServiceProvider();
        var sut = new ExtensibleCSharpMigrationOperationGenerator(spy, sp);
        var builder = new IndentedStringBuilder();

        var ops = new MigrationOperation[]
        {
            new AddColumnOperation { Name = "X", Table = "T" },
        };
        sut.Generate("mb", ops, builder);

        Assert.Single(spy.Calls);
        Assert.Single(spy.Calls[0]);
    }

    private static IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddTransient<ICSharpMigrationOperationHandler, TestPhasedHandler>();
        return services.BuildServiceProvider();
    }

    [CustomMigrationHandler(Order = 100)]
    public sealed class TestPhasedHandler : ICSharpMigrationOperationHandler
    {
        public bool CanHandle(MigrationOperation operation) =>
            operation is SpyOperation or DropSpyOperation;

        public OperationPhase Phase(MigrationOperation operation) =>
            operation is DropSpyOperation ? OperationPhase.BeforeCore : OperationPhase.AfterCore;

        public void Generate(MigrationOperation operation, IndentedStringBuilder builder)
        {
            switch (operation)
            {
                case SpyOperation s:
                    builder.AppendLine($"// AFTER:{s.Marker}");
                    break;
                case DropSpyOperation d:
                    builder.AppendLine($"// BEFORE:{d.Marker}");
                    break;
            }
        }
    }
}
