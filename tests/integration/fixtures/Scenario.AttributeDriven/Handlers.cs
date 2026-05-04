using EntityFrameworkCore.ExtensibleMigrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Scenario.AttributeDriven;

[CustomMigrationHandler(Order = 200)]
public sealed class FullTextOperationHandler : IMigrationOperationHandler
{
    public bool HasDifferences(
        IRelationalModel? source,
        IRelationalModel? target,
        bool defaultHasDifferences
    ) =>
        Indexes(target)
            .Select(i => i.IndexName)
            .Except(Indexes(source).Select(i => i.IndexName))
            .Any()
        || Indexes(source)
            .Select(i => i.IndexName)
            .Except(Indexes(target).Select(i => i.IndexName))
            .Any();

    public IReadOnlyList<MigrationOperation> GetOperations(
        IRelationalModel? source,
        IRelationalModel? target,
        IReadOnlyList<MigrationOperation> existing
    )
    {
        var ops = new List<MigrationOperation>();
        var src = Indexes(source).ToDictionary(i => i.IndexName);
        var tgt = Indexes(target).ToDictionary(i => i.IndexName);

        foreach (var (name, info) in tgt)
        {
            if (src.ContainsKey(name))
                continue;
            ops.Add(
                new CreateFullTextIndexOperation
                {
                    TableName = info.TableName,
                    ColumnName = info.ColumnName,
                    IndexName = name,
                    Language = info.Language,
                }
            );
        }
        foreach (var (name, info) in src)
        {
            if (tgt.ContainsKey(name))
                continue;
            ops.Add(
                new DropFullTextIndexOperation { TableName = info.TableName, IndexName = name }
            );
        }
        return ops;
    }

    private static IEnumerable<(
        string IndexName,
        string TableName,
        string ColumnName,
        string Language
    )> Indexes(IRelationalModel? m)
    {
        if (m is null)
            yield break;
        foreach (var et in m.Model.GetEntityTypes())
        {
            var tableName = et.GetTableName();
            var schema = et.GetSchema();
            if (tableName is null)
                continue;
            var table = m.FindTable(tableName, schema);
            if (table is null)
                continue;

            foreach (var p in et.GetProperties())
            {
                if (p.FindAnnotation("FullText:IsFullText") is not { Value: true })
                    continue;
                var lang = p.FindAnnotation("FullText:Language")?.Value as string ?? "english";
                var col = table.FindColumn(p);
                if (col is null)
                    continue;
                var indexName = $"ix_ft_{tableName}_{col.Name}";
                yield return (indexName, tableName, col.Name, lang);
            }
        }
    }
}

[CustomMigrationHandler(Order = 200)]
public sealed class FullTextCSharpHandler : ICSharpMigrationOperationHandler
{
    public bool CanHandle(MigrationOperation op) =>
        op is CreateFullTextIndexOperation or DropFullTextIndexOperation;

    // Index depends on the table → create after, drop before.
    public OperationPhase Phase(MigrationOperation op) =>
        op is DropFullTextIndexOperation ? OperationPhase.BeforeCore : OperationPhase.AfterCore;

    public void Generate(MigrationOperation op, IndentedStringBuilder builder)
    {
        switch (op)
        {
            case CreateFullTextIndexOperation c:
                builder.AppendLine(
                    $"migrationBuilder.Sql(\"CREATE INDEX \\\"{c.IndexName}\\\" ON \\\"{c.TableName}\\\" USING gin (to_tsvector('{c.Language}', \\\"{c.ColumnName}\\\"));\");"
                );
                break;
            case DropFullTextIndexOperation d:
                builder.AppendLine(
                    $"migrationBuilder.Sql(\"DROP INDEX IF EXISTS \\\"{d.IndexName}\\\";\");"
                );
                break;
        }
    }
}

// No IMigrationsSnapshotHandler — FullText:* property annotations are
// auto-serialised by EF Core's default snapshot writer.
