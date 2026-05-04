using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace EntityFrameworkCore.ExtensibleMigrations.Tests.Stubs;

public sealed class SpyCSharpMigrationOperationGenerator : ICSharpMigrationOperationGenerator
{
    public List<IReadOnlyList<MigrationOperation>> Calls { get; } = new();

    public void Generate(
        string builderName,
        IReadOnlyList<MigrationOperation> operations,
        IndentedStringBuilder builder
    )
    {
        Calls.Add(operations.ToList());
        foreach (var op in operations)
        {
            builder.AppendLine($"// CORE:{op.GetType().Name}");
        }
    }
}
