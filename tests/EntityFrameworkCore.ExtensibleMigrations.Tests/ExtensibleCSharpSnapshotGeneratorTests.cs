using EntityFrameworkCore.ExtensibleMigrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.ExtensibleMigrations.Tests;

public class ExtensibleCSharpSnapshotGeneratorTests
{
    [Fact]
    public void Calls_inner_generator_then_appends_handler_output()
    {
        var inner = new SpySnapshotGenerator();
        var sp = new ServiceCollection()
            .AddTransient<IMigrationsSnapshotHandler, TestSnapshotHandler>()
            .BuildServiceProvider();
        var sut = new ExtensibleCSharpSnapshotGenerator(inner, sp);
        var builder = new IndentedStringBuilder();

        var modelBuilder = new ModelBuilder();
        sut.Generate("mb", (IModel)modelBuilder.Model, builder);

        var output = builder.ToString();
        var innerIdx = output.IndexOf("INNER:", StringComparison.Ordinal);
        var handlerIdx = output.IndexOf(
            "HANDLER:Searchable:CreatedIndex",
            StringComparison.Ordinal
        );

        Assert.True(innerIdx >= 0);
        Assert.True(handlerIdx > innerIdx, "Handler must run after inner generator");
    }

    private sealed class SpySnapshotGenerator : ICSharpSnapshotGenerator
    {
        public void Generate(string builderName, IModel model, IndentedStringBuilder builder) =>
            builder.AppendLine("// INNER:" + builderName);
    }

    [CustomMigrationHandler(Order = 100)]
    public sealed class TestSnapshotHandler : IMigrationsSnapshotHandler
    {
        public void GenerateSnapshot(IModel model, IndentedStringBuilder builder) =>
            builder.AppendLine("// HANDLER:Searchable:CreatedIndex:ix_foo");
    }
}
