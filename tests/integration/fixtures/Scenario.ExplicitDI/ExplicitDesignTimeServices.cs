using EntityFrameworkCore.ExtensibleMigrations;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Scenario.ExplicitDI;

/// <summary>
/// Registers handlers explicitly via the public service-collection extensions
/// instead of relying on <c>[CustomMigrationHandler]</c> attribute discovery.
/// Forwards to <see cref="ExtensibleMigrationsDesignTimeServices"/> first so
/// the wrappers are wired up, then adds handler types.
/// </summary>
public sealed class ExplicitDesignTimeServices : IDesignTimeServices
{
    public void ConfigureDesignTimeServices(IServiceCollection services)
    {
        new ExtensibleMigrationsDesignTimeServices().ConfigureDesignTimeServices(services);
        services.AddMigrationOperationHandler<TaggedMarkerOperationHandler>();
        services.AddCSharpMigrationOperationHandler<TaggedMarkerCSharpHandler>();
    }
}
