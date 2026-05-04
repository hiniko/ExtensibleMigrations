using EntityFrameworkCore.ExtensibleMigrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Scenario.BeforeCorePhase;

[CustomMigrationHandler(Order = 100)]
public sealed class PgExtensionOperationHandler : IMigrationOperationHandler
{
    public bool HasDifferences(
        IRelationalModel? source,
        IRelationalModel? target,
        bool defaultHasDifferences
    ) =>
        Extensions(target).Except(Extensions(source)).Any()
        || Extensions(source).Except(Extensions(target)).Any();

    public IReadOnlyList<MigrationOperation> GetOperations(
        IRelationalModel? source,
        IRelationalModel? target,
        IReadOnlyList<MigrationOperation> existing
    )
    {
        var ops = new List<MigrationOperation>();
        foreach (var ext in Extensions(target).Except(Extensions(source)))
            ops.Add(new EnsurePostgresExtensionOperation { ExtensionName = ext });
        foreach (var ext in Extensions(source).Except(Extensions(target)))
            ops.Add(new DropPostgresExtensionOperation { ExtensionName = ext });
        return ops;
    }

    private static IEnumerable<string> Extensions(IRelationalModel? m)
    {
        if (m is null)
            return Array.Empty<string>();
        return m.Model.FindAnnotation("Pg:Extensions")?.Value as IEnumerable<string>
            ?? Array.Empty<string>();
    }
}

[CustomMigrationHandler(Order = 100)]
public sealed class PgExtensionCSharpHandler : ICSharpMigrationOperationHandler
{
    public bool CanHandle(MigrationOperation op) =>
        op is EnsurePostgresExtensionOperation or DropPostgresExtensionOperation;

    // CREATE EXTENSION must run BEFORE any table is created (BeforeCore in Up).
    // DROP EXTENSION must run AFTER all dependent tables are dropped
    // (AfterCore in Down — EF passes Down ops in reverse, but phase buckets
    // are direction-agnostic, so we encode the dependency direction per op type).
    public OperationPhase Phase(MigrationOperation op) =>
        op is DropPostgresExtensionOperation ? OperationPhase.AfterCore : OperationPhase.BeforeCore;

    public void Generate(MigrationOperation op, IndentedStringBuilder builder)
    {
        switch (op)
        {
            case EnsurePostgresExtensionOperation e:
                builder.AppendLine(
                    $"migrationBuilder.Sql(\"CREATE EXTENSION IF NOT EXISTS \\\"{e.ExtensionName}\\\";\");"
                );
                break;
            case DropPostgresExtensionOperation d:
                builder.AppendLine(
                    $"migrationBuilder.Sql(\"DROP EXTENSION IF EXISTS \\\"{d.ExtensionName}\\\";\");"
                );
                break;
        }
    }
}

// No IMigrationsSnapshotHandler — Pg:Extensions is a model-level annotation
// EF Core's default snapshot writer serialises automatically.
