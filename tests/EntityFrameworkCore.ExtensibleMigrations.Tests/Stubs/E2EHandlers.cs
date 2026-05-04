using EntityFrameworkCore.ExtensibleMigrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace EntityFrameworkCore.ExtensibleMigrations.Tests.Stubs;

public sealed class E2EMarkerOperation : MigrationOperation
{
    public string Marker { get; init; } = "";
}

[CustomMigrationHandler(Order = 100)]
public sealed class E2EOperationHandler : IMigrationOperationHandler
{
    public bool HasDifferences(
        IRelationalModel? s,
        IRelationalModel? t,
        bool defaultHasDifferences
    ) => defaultHasDifferences;

    public IReadOnlyList<MigrationOperation> GetOperations(
        IRelationalModel? s,
        IRelationalModel? t,
        IReadOnlyList<MigrationOperation> existing
    )
    {
        if (t is null)
            return Array.Empty<MigrationOperation>();
        return new MigrationOperation[] { new E2EMarkerOperation { Marker = "E2E_OP_EMITTED" } };
    }
}

[CustomMigrationHandler(Order = 100)]
public sealed class E2ECSharpHandler : ICSharpMigrationOperationHandler
{
    public bool CanHandle(MigrationOperation op) => op is E2EMarkerOperation;

    public OperationPhase Phase(MigrationOperation op) => OperationPhase.AfterCore;

    public void Generate(MigrationOperation op, IndentedStringBuilder builder)
    {
        var m = (E2EMarkerOperation)op;
        builder.AppendLine($"migrationBuilder.Sql(\"-- {m.Marker}\");");
    }
}

[CustomMigrationHandler(Order = 100)]
public sealed class E2ESnapshotHandler : IMigrationsSnapshotHandler
{
    public void GenerateSnapshot(IModel model, IndentedStringBuilder builder) =>
        builder.AppendLine("// E2E_SNAPSHOT_HANDLER_RAN");
}
