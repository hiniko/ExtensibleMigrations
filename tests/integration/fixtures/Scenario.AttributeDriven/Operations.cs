using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Scenario.AttributeDriven;

/// <summary>
/// Compact DTO carrying only what the handler needs to build the SQL.
/// No SQL strings stored on the operation — the handler renders them.
/// </summary>
public sealed class CreateFullTextIndexOperation : MigrationOperation
{
    public string TableName { get; init; } = "";
    public string ColumnName { get; init; } = "";
    public string IndexName { get; init; } = "";
    public string Language { get; init; } = "";
}

public sealed class DropFullTextIndexOperation : MigrationOperation
{
    public string TableName { get; init; } = "";
    public string IndexName { get; init; } = "";
}
