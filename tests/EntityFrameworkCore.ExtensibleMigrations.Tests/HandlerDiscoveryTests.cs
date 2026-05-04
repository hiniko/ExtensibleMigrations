using System.Reflection;
using EntityFrameworkCore.ExtensibleMigrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.ExtensibleMigrations.Tests;

public class HandlerDiscoveryTests
{
    private static readonly Assembly TestAssembly = typeof(HandlerDiscoveryTests).Assembly;

    [Fact]
    public void Registers_operation_handler_marked_with_attribute()
    {
        var services = new ServiceCollection();
        services.AddExtensibleMigrationsFromAssembly(TestAssembly);

        var registered = services
            .Where(s => s.ServiceType == typeof(IMigrationOperationHandler))
            .Select(s => s.ImplementationType)
            .ToList();

        Assert.Contains(typeof(StubOperationHandler), registered);
    }

    [Fact]
    public void Registers_csharp_handler_marked_with_attribute()
    {
        var services = new ServiceCollection();
        services.AddExtensibleMigrationsFromAssembly(TestAssembly);

        var registered = services
            .Where(s => s.ServiceType == typeof(ICSharpMigrationOperationHandler))
            .Select(s => s.ImplementationType)
            .ToList();

        Assert.Contains(typeof(StubCSharpHandler), registered);
    }

    [Fact]
    public void Skips_classes_without_attribute()
    {
        var services = new ServiceCollection();
        services.AddExtensibleMigrationsFromAssembly(TestAssembly);

        var registered = services
            .Where(s => s.ServiceType == typeof(IMigrationOperationHandler))
            .Select(s => s.ImplementationType)
            .ToList();

        Assert.DoesNotContain(typeof(UntaggedHandler), registered);
    }
}

[CustomMigrationHandler(Order = 500)]
public sealed class StubOperationHandler : IMigrationOperationHandler
{
    public bool HasDifferences(
        IRelationalModel? source,
        IRelationalModel? target,
        bool defaultHasDifferences
    ) => defaultHasDifferences;

    public IReadOnlyList<MigrationOperation> GetOperations(
        IRelationalModel? source,
        IRelationalModel? target,
        IReadOnlyList<MigrationOperation> existingOperations
    ) => Array.Empty<MigrationOperation>();
}

[CustomMigrationHandler(Order = 500)]
public sealed class StubCSharpHandler : ICSharpMigrationOperationHandler
{
    public bool CanHandle(MigrationOperation operation) => false;

    public void Generate(MigrationOperation operation, IndentedStringBuilder builder) { }
}

public sealed class UntaggedHandler : IMigrationOperationHandler
{
    public bool HasDifferences(
        IRelationalModel? source,
        IRelationalModel? target,
        bool defaultHasDifferences
    ) => false;

    public IReadOnlyList<MigrationOperation> GetOperations(
        IRelationalModel? source,
        IRelationalModel? target,
        IReadOnlyList<MigrationOperation> existingOperations
    ) => Array.Empty<MigrationOperation>();
}
