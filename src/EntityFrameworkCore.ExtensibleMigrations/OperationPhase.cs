namespace EntityFrameworkCore.ExtensibleMigrations;

/// <summary>
/// Where a custom migration operation should appear relative to core EF Core operations
/// in the generated C# migration body.
/// </summary>
public enum OperationPhase
{
    /// <summary>
    /// Emit before EF's core operations. Use for drops, prerequisite extension installs, etc.
    /// </summary>
    BeforeCore,

    /// <summary>
    /// Emit after EF's core operations. Use for indexes, views, grants — anything that
    /// depends on tables/columns existing.
    /// </summary>
    AfterCore,
}
