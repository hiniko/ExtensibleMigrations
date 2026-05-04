using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Scenario.MultiExtension;

public sealed class CreateMaterializedViewOperation : MigrationOperation
{
    public string ViewName { get; init; } = "";
    public string Query { get; init; } = "";
}

public sealed class DropMaterializedViewOperation : MigrationOperation
{
    public string ViewName { get; init; } = "";
}

public sealed class CreateGinIndexOperation : MigrationOperation
{
    public string TableName { get; init; } = "";
    public string ColumnName { get; init; } = "";
    public string IndexName { get; init; } = "";
}

public sealed class DropGinIndexOperation : MigrationOperation
{
    public string IndexName { get; init; } = "";
}
