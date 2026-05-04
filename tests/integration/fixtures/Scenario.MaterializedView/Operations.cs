using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Scenario.MaterializedView;

public sealed class CreateMaterializedViewOperation : MigrationOperation
{
    public string ViewName { get; init; } = "";
    public string Query { get; init; } = "";
}

public sealed class DropMaterializedViewOperation : MigrationOperation
{
    public string ViewName { get; init; } = "";
}
