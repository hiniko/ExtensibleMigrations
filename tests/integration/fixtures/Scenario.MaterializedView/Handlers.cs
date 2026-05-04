using EntityFrameworkCore.ExtensibleMigrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Scenario.MaterializedView;

[CustomMigrationHandler(Order = 200)]
public sealed class MaterializedViewOperationHandler : IMigrationOperationHandler
{
    public bool HasDifferences(
        IRelationalModel? source,
        IRelationalModel? target,
        bool defaultHasDifferences
    ) => Views(target).Except(Views(source)).Any() || Views(source).Except(Views(target)).Any();

    public IReadOnlyList<MigrationOperation> GetOperations(
        IRelationalModel? source,
        IRelationalModel? target,
        IReadOnlyList<MigrationOperation> existing
    )
    {
        var ops = new List<MigrationOperation>();
        foreach (var (name, query) in Views(target).Except(Views(source)))
            ops.Add(new CreateMaterializedViewOperation { ViewName = name, Query = query });
        foreach (var (name, _) in Views(source).Except(Views(target)))
            ops.Add(new DropMaterializedViewOperation { ViewName = name });
        return ops;
    }

    private static IEnumerable<(string Name, string Query)> Views(IRelationalModel? m)
    {
        if (m is null)
            yield break;
        foreach (var et in m.Model.GetEntityTypes())
        {
            var n = et.FindAnnotation("MatView:Name")?.Value as string;
            var q = et.FindAnnotation("MatView:Query")?.Value as string;
            if (n is not null && q is not null)
                yield return (n, q);
        }
    }
}

[CustomMigrationHandler(Order = 200)]
public sealed class MaterializedViewCSharpHandler : ICSharpMigrationOperationHandler
{
    public bool CanHandle(MigrationOperation op) =>
        op is CreateMaterializedViewOperation or DropMaterializedViewOperation;

    public OperationPhase Phase(MigrationOperation op) =>
        op is DropMaterializedViewOperation ? OperationPhase.BeforeCore : OperationPhase.AfterCore;

    public void Generate(MigrationOperation op, IndentedStringBuilder builder)
    {
        switch (op)
        {
            case CreateMaterializedViewOperation c:
                var escapedQuery = c.Query.Replace("\"", "\\\"");
                builder.AppendLine(
                    $"migrationBuilder.Sql(\"CREATE MATERIALIZED VIEW \\\"{c.ViewName}\\\" AS {escapedQuery};\");"
                );
                break;
            case DropMaterializedViewOperation d:
                builder.AppendLine(
                    $"migrationBuilder.Sql(\"DROP MATERIALIZED VIEW IF EXISTS \\\"{d.ViewName}\\\";\");"
                );
                break;
        }
    }
}

// No IMigrationsSnapshotHandler — the MatView:* annotations sit on a keyless
// ToView entity, which EF Core's default snapshot writer auto-serialises.
// See Scenario.SnapshotOnly for the case where a snapshot handler is needed.
