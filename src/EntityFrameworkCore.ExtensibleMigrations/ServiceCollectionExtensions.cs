using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.ExtensibleMigrations;

/// <summary>
/// Extensions for registering ExtensibleMigrations handlers with the design-time service collection.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMigrationOperationHandler<T>(
        this IServiceCollection services
    )
        where T : class, IMigrationOperationHandler =>
        services.AddTransient<IMigrationOperationHandler, T>();

    public static IServiceCollection AddCSharpMigrationOperationHandler<T>(
        this IServiceCollection services
    )
        where T : class, ICSharpMigrationOperationHandler =>
        services.AddTransient<ICSharpMigrationOperationHandler, T>();

    public static IServiceCollection AddMigrationsSnapshotHandler<T>(
        this IServiceCollection services
    )
        where T : class, IMigrationsSnapshotHandler =>
        services.AddTransient<IMigrationsSnapshotHandler, T>();

    /// <summary>
    /// Discovers handler types in the given assembly via [CustomMigrationHandler] attributes
    /// and registers them under the appropriate handler interface.
    /// </summary>
    public static IServiceCollection AddExtensibleMigrationsFromAssembly(
        this IServiceCollection services,
        Assembly assembly
    )
    {
        foreach (var type in HandlerDiscovery.SafeGetTypes(assembly))
        {
            if (type is not { IsAbstract: false, IsInterface: false })
                continue;
            if (type.GetCustomAttribute<CustomMigrationHandlerAttribute>() is null)
                continue;

            if (typeof(IMigrationOperationHandler).IsAssignableFrom(type))
                services.AddTransient(typeof(IMigrationOperationHandler), type);
            if (typeof(ICSharpMigrationOperationHandler).IsAssignableFrom(type))
                services.AddTransient(typeof(ICSharpMigrationOperationHandler), type);
            if (typeof(IMigrationsSnapshotHandler).IsAssignableFrom(type))
                services.AddTransient(typeof(IMigrationsSnapshotHandler), type);
        }
        return services;
    }
}
