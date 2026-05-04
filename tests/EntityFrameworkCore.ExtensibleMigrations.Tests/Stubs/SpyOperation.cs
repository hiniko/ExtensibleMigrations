using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace EntityFrameworkCore.ExtensibleMigrations.Tests.Stubs;

public sealed class SpyOperation : MigrationOperation
{
    public string Marker { get; init; } = "";
}

public sealed class DropSpyOperation : MigrationOperation
{
    public string Marker { get; init; } = "";
}
