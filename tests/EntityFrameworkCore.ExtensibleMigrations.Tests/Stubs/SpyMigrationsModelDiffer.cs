using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace EntityFrameworkCore.ExtensibleMigrations.Tests.Stubs;

public sealed class SpyMigrationsModelDiffer : IMigrationsModelDiffer
{
    public List<string> Calls { get; } = new();
    public bool ReturnHasDifferences { get; set; }
    public IReadOnlyList<MigrationOperation> ReturnDifferences { get; set; } =
        Array.Empty<MigrationOperation>();

    public bool HasDifferences(IRelationalModel? source, IRelationalModel? target)
    {
        Calls.Add(nameof(HasDifferences));
        return ReturnHasDifferences;
    }

    public IReadOnlyList<MigrationOperation> GetDifferences(
        IRelationalModel? source,
        IRelationalModel? target
    )
    {
        Calls.Add(nameof(GetDifferences));
        return ReturnDifferences;
    }
}
