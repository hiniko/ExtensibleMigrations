using System.Reflection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.ExtensibleMigrations;

/// <summary>
/// Wraps the default <see cref="ICSharpSnapshotGenerator"/> so registered
/// <see cref="IMigrationsSnapshotHandler"/>s can append their own state to the snapshot.
/// </summary>
internal sealed class ExtensibleCSharpSnapshotGenerator(
    ICSharpSnapshotGenerator inner,
    IServiceProvider serviceProvider
) : ICSharpSnapshotGenerator
{
    private readonly IReadOnlyList<IMigrationsSnapshotHandler> _handlers = serviceProvider
        .GetServices<IMigrationsSnapshotHandler>()
        .OrderBy(h =>
            h.GetType().GetCustomAttribute<CustomMigrationHandlerAttribute>()?.Order ?? 1000
        )
        .ToList();

    public void Generate(string builderName, IModel model, IndentedStringBuilder builder)
    {
        inner.Generate(builderName, model, builder);
        foreach (var h in _handlers)
        {
            h.GenerateSnapshot(model, builder);
        }
    }
}
