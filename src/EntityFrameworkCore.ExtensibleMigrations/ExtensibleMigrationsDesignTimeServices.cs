using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.ExtensibleMigrations;

/// <summary>
/// EF Core design-time services entry point. Wraps the default
/// <see cref="IMigrationsModelDiffer"/> and <see cref="ICSharpMigrationOperationGenerator"/>
/// so attribute-discovered handlers can contribute custom operations.
/// </summary>
public sealed class ExtensibleMigrationsDesignTimeServices : IDesignTimeServices
{
    public void ConfigureDesignTimeServices(IServiceCollection services)
    {
        RegisterHandlerTypes(services);
        WrapService<IMigrationsModelDiffer>(
            services,
            required: true,
            (inner, sp) => new ExtensibleMigrationsModelDiffer(inner, sp)
        );
        WrapService<ICSharpMigrationOperationGenerator>(
            services,
            required: false,
            (inner, sp) => new ExtensibleCSharpMigrationOperationGenerator(inner, sp)
        );
        WrapService<ICSharpSnapshotGenerator>(
            services,
            required: false,
            (inner, sp) => new ExtensibleCSharpSnapshotGenerator(inner, sp)
        );
    }

    private static void RegisterHandlerTypes(IServiceCollection services)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            services.AddExtensibleMigrationsFromAssembly(assembly);
        }
    }

    private static void WrapService<TService>(
        IServiceCollection services,
        bool required,
        Func<TService, IServiceProvider, TService> wrap
    )
        where TService : class
    {
        var descriptor = services.FirstOrDefault(s => s.ServiceType == typeof(TService));
        if (descriptor is null)
        {
            if (required)
            {
                throw new InvalidOperationException(
                    $"Cannot find existing registration for {typeof(TService).Name}. "
                        + "Expected EF Core to have registered it before design-time services run."
                );
            }
            return;
        }

        services.Remove(descriptor);
        services.Add(
            new ServiceDescriptor(
                typeof(TService),
                sp => wrap(BuildOriginal<TService>(sp, descriptor), sp),
                descriptor.Lifetime
            )
        );
    }

    private static TService BuildOriginal<TService>(IServiceProvider sp, ServiceDescriptor d)
        where TService : class
    {
        if (d.ImplementationFactory is not null)
            return (TService)d.ImplementationFactory(sp);
        if (d.ImplementationInstance is not null)
            return (TService)d.ImplementationInstance;
        if (d.ImplementationType is not null)
            return (TService)ActivatorUtilities.CreateInstance(sp, d.ImplementationType);

        throw new InvalidOperationException(
            $"ServiceDescriptor for {typeof(TService).Name} has no factory, instance, or type."
        );
    }
}
