using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace EntityFrameworkCore.ExtensibleMigrations;

/// <summary>
/// Interface for handlers that generate C# code for migration operations during scaffolding.
/// </summary>
public interface ICSharpMigrationOperationHandler
{
    /// <summary>
    /// Determines if this handler can generate C# code for the given operation.
    /// </summary>
    bool CanHandle(MigrationOperation operation);

    /// <summary>
    /// Where this operation should appear relative to core EF operations.
    /// Default: <see cref="OperationPhase.AfterCore"/>.
    /// </summary>
    OperationPhase Phase(MigrationOperation operation) => OperationPhase.AfterCore;

    /// <summary>
    /// Generates C# code for the given operation.
    /// </summary>
    void Generate(MigrationOperation operation, IndentedStringBuilder builder);
}
