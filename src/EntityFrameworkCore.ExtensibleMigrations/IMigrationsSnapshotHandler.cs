using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityFrameworkCore.ExtensibleMigrations;

/// <summary>
/// Handler that appends additional snapshot code after the default snapshot generator.
/// Use to write modelBuilder.HasAnnotation(...) calls or similar so handler-managed state
/// becomes part of the source model on the next migration diff.
/// </summary>
public interface IMigrationsSnapshotHandler
{
    /// <summary>
    /// Appends snapshot code. Must be deterministic — same model in, same output out.
    /// </summary>
    void GenerateSnapshot(IModel model, IndentedStringBuilder builder);
}
