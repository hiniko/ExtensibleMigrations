using EntityFrameworkCore.ExtensibleMigrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Scenario.MultiExtension;

[CustomMigrationHandler(Order = 200)]
public sealed class MatViewHandler : IMigrationOperationHandler
{
    public bool HasDifferences(IRelationalModel? s, IRelationalModel? t, bool d) =>
        Views(t).Except(Views(s)).Any() || Views(s).Except(Views(t)).Any();

    public IReadOnlyList<MigrationOperation> GetOperations(
        IRelationalModel? s,
        IRelationalModel? t,
        IReadOnlyList<MigrationOperation> e
    )
    {
        var ops = new List<MigrationOperation>();
        foreach (var (n, q) in Views(t).Except(Views(s)))
            ops.Add(new CreateMaterializedViewOperation { ViewName = n, Query = q });
        foreach (var (n, _) in Views(s).Except(Views(t)))
            ops.Add(new DropMaterializedViewOperation { ViewName = n });
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

[CustomMigrationHandler(Order = 300)]
public sealed class GinIndexHandler : IMigrationOperationHandler
{
    public bool HasDifferences(IRelationalModel? s, IRelationalModel? t, bool d) =>
        Indexes(t).Except(Indexes(s)).Any() || Indexes(s).Except(Indexes(t)).Any();

    public IReadOnlyList<MigrationOperation> GetOperations(
        IRelationalModel? s,
        IRelationalModel? t,
        IReadOnlyList<MigrationOperation> e
    )
    {
        var ops = new List<MigrationOperation>();
        foreach (var (table, col, idx) in Indexes(t).Except(Indexes(s)))
            ops.Add(
                new CreateGinIndexOperation
                {
                    TableName = table,
                    ColumnName = col,
                    IndexName = idx,
                }
            );
        foreach (var (_, _, idx) in Indexes(s).Except(Indexes(t)))
            ops.Add(new DropGinIndexOperation { IndexName = idx });
        return ops;
    }

    private static IEnumerable<(string Table, string Col, string Idx)> Indexes(IRelationalModel? m)
    {
        if (m is null)
            yield break;
        foreach (var et in m.Model.GetEntityTypes())
        {
            var table = et.GetTableName();
            if (table is null)
                continue;
            foreach (var p in et.GetProperties())
            {
                if (p.FindAnnotation("PgIndex:Gin") is { Value: true })
                {
                    var col = p.GetColumnName();
                    yield return (table, col, $"ix_gin_{table}_{col}");
                }
            }
        }
    }
}

[CustomMigrationHandler(Order = 200)]
public sealed class MatViewCSharpHandler : ICSharpMigrationOperationHandler
{
    public bool CanHandle(MigrationOperation op) =>
        op is CreateMaterializedViewOperation or DropMaterializedViewOperation;

    public OperationPhase Phase(MigrationOperation op) =>
        op is DropMaterializedViewOperation ? OperationPhase.BeforeCore : OperationPhase.AfterCore;

    public void Generate(MigrationOperation op, IndentedStringBuilder b)
    {
        switch (op)
        {
            case CreateMaterializedViewOperation c:
                var query = c.Query.Replace("\"", "\\\"");
                b.AppendLine(
                    $"migrationBuilder.Sql(\"CREATE MATERIALIZED VIEW \\\"{c.ViewName}\\\" AS {query};\");"
                );
                break;
            case DropMaterializedViewOperation d:
                b.AppendLine(
                    $"migrationBuilder.Sql(\"DROP MATERIALIZED VIEW IF EXISTS \\\"{d.ViewName}\\\";\");"
                );
                break;
        }
    }
}

[CustomMigrationHandler(Order = 300)]
public sealed class GinIndexCSharpHandler : ICSharpMigrationOperationHandler
{
    public bool CanHandle(MigrationOperation op) =>
        op is CreateGinIndexOperation or DropGinIndexOperation;

    public OperationPhase Phase(MigrationOperation op) =>
        op is DropGinIndexOperation ? OperationPhase.BeforeCore : OperationPhase.AfterCore;

    public void Generate(MigrationOperation op, IndentedStringBuilder b)
    {
        switch (op)
        {
            case CreateGinIndexOperation c:
                b.AppendLine(
                    $"migrationBuilder.Sql(\"CREATE INDEX \\\"{c.IndexName}\\\" ON \\\"{c.TableName}\\\" USING gin (to_tsvector('english', \\\"{c.ColumnName}\\\"));\");"
                );
                break;
            case DropGinIndexOperation d:
                b.AppendLine(
                    $"migrationBuilder.Sql(\"DROP INDEX IF EXISTS \\\"{d.IndexName}\\\";\");"
                );
                break;
        }
    }
}
