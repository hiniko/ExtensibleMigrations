using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Scenario.BeforeCorePhase;

public sealed class EnsurePostgresExtensionOperation : MigrationOperation
{
    public string ExtensionName { get; init; } = "";
}

public sealed class DropPostgresExtensionOperation : MigrationOperation
{
    public string ExtensionName { get; init; } = "";
}
