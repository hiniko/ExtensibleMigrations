using EntityFrameworkCore.ExtensibleMigrations;
using EntityFrameworkCore.ExtensibleMigrations.Tests.Stubs;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.ExtensibleMigrations.Tests;

public class ExtensibleMigrationsDesignTimeServicesTests
{
    [Fact]
    public void Preserves_scoped_lifetime_of_original_differ()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMigrationsModelDiffer, SpyMigrationsModelDiffer>();

        new ExtensibleMigrationsDesignTimeServices().ConfigureDesignTimeServices(services);

        var differDescriptor = services.Single(s =>
            s.ServiceType == typeof(IMigrationsModelDiffer)
        );
        Assert.Equal(ServiceLifetime.Scoped, differDescriptor.Lifetime);
    }

    [Fact]
    public void Preserves_singleton_lifetime_of_original_csharp_generator()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMigrationsModelDiffer, SpyMigrationsModelDiffer>();
        services.AddSingleton<
            ICSharpMigrationOperationGenerator,
            SpyCSharpMigrationOperationGenerator
        >();

        new ExtensibleMigrationsDesignTimeServices().ConfigureDesignTimeServices(services);

        var d = services.Single(s => s.ServiceType == typeof(ICSharpMigrationOperationGenerator));
        Assert.Equal(ServiceLifetime.Singleton, d.Lifetime);
    }

    [Fact]
    public void Throws_when_IMigrationsModelDiffer_not_registered()
    {
        var services = new ServiceCollection();
        var sut = new ExtensibleMigrationsDesignTimeServices();
        Assert.Throws<InvalidOperationException>(() => sut.ConfigureDesignTimeServices(services));
    }

    [Fact]
    public void Skips_csharp_generator_replacement_when_not_registered()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMigrationsModelDiffer, SpyMigrationsModelDiffer>();

        new ExtensibleMigrationsDesignTimeServices().ConfigureDesignTimeServices(services);

        Assert.DoesNotContain(
            services,
            s => s.ServiceType == typeof(ICSharpMigrationOperationGenerator)
        );
    }
}
