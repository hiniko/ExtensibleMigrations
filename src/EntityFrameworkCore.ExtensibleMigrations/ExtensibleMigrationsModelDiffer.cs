using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.ExtensibleMigrations;

/// <summary>
/// A Wrapper around the default EF MigrationsModelDiffer that allows for extensibility via custom operation handlers.
/// Operation handlers can contribute additional migration operations based on differences between the source and target models.
/// </summary>
internal sealed class ExtensibleMigrationsModelDiffer(
    IMigrationsModelDiffer defaultDiffer,
    IServiceProvider serviceProvider
) : IMigrationsModelDiffer
{
    private readonly IReadOnlyList<IMigrationOperationHandler> _handlers = serviceProvider
        .GetServices<IMigrationOperationHandler>()
        .OrderBy(h =>
            h.GetType().GetCustomAttribute<CustomMigrationHandlerAttribute>()?.Order ?? 1000
        )
        .ToList();

    public bool HasDifferences(IRelationalModel? source, IRelationalModel? target)
    {
        // Check default differ first
        var defaultHasDifferences = defaultDiffer.HasDifferences(source, target);

        // Check all handlers
        return _handlers.Any(handler =>
                handler.HasDifferences(source, target, defaultHasDifferences)
            ) || defaultHasDifferences;
    }

    public IReadOnlyList<MigrationOperation> GetDifferences(
        IRelationalModel? source,
        IRelationalModel? target
    )
    {
        // Get default operations
        var operations = defaultDiffer.GetDifferences(source, target).ToList();

        // Let each handler contribute additional operations
        foreach (var handler in _handlers)
        {
            var handlerOperations = handler.GetOperations(source, target, operations);
            operations.AddRange(handlerOperations);
        }

        return operations;
    }
}
