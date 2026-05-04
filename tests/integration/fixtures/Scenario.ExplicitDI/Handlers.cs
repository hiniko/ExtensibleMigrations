using EntityFrameworkCore.ExtensibleMigrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Scenario.ExplicitDI;

// Note: NO [CustomMigrationHandler] attribute. Registered explicitly via DI
// in ExplicitDesignTimeServices below — this exercises the
// AddCSharpMigrationOperationHandler<T>() / AddMigrationOperationHandler<T>()
// public extension methods rather than attribute-based discovery.

public sealed class TaggedMarkerOperationHandler : IMigrationOperationHandler
{
    public bool HasDifferences(
        IRelationalModel? source,
        IRelationalModel? target,
        bool defaultHasDifferences
    ) => Markers(target).Except(Markers(source)).Any();

    public IReadOnlyList<MigrationOperation> GetOperations(
        IRelationalModel? source,
        IRelationalModel? target,
        IReadOnlyList<MigrationOperation> existing
    ) =>
        Markers(target)
            .Except(Markers(source))
            .Select(m =>
                (MigrationOperation)
                    new TaggedMarkerOperation { EntityName = m.EntityName, Marker = m.Marker }
            )
            .ToList();

    private static IEnumerable<(string EntityName, string Marker)> Markers(IRelationalModel? m)
    {
        if (m is null)
            yield break;
        foreach (var et in m.Model.GetEntityTypes())
        {
            if (et.FindAnnotation("Tagged:Marker")?.Value is string marker)
                yield return (et.Name, marker);
        }
    }
}

public sealed class TaggedMarkerCSharpHandler : ICSharpMigrationOperationHandler
{
    public bool CanHandle(MigrationOperation op) => op is TaggedMarkerOperation;

    public OperationPhase Phase(MigrationOperation op) => OperationPhase.AfterCore;

    public void Generate(MigrationOperation op, IndentedStringBuilder builder)
    {
        var t = (TaggedMarkerOperation)op;
        builder.AppendLine($"// TaggedMarker: {t.EntityName} -> {t.Marker}");
    }
}
