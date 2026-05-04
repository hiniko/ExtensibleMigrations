namespace EntityFrameworkCore.ExtensibleMigrations;

/// <summary>
/// Marks a class implementing <see cref="IMigrationOperationHandler"/> or
/// <see cref="ICSharpMigrationOperationHandler"/> for attribute-based discovery.
/// Alternatively register handlers explicitly via the DI extensions.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CustomMigrationHandlerAttribute : Attribute
{
    /// <summary>
    /// Order of execution. Lower runs first. Default 1000.
    /// </summary>
    public int Order { get; init; } = 1000;
}
