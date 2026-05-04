using Microsoft.EntityFrameworkCore;

namespace Scenario.MultiExtension;

public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Sku { get; set; } = "";
}

public sealed class ProductContext : DbContext
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnConfiguring(DbContextOptionsBuilder o)
    {
        var conn =
            Environment.GetEnvironmentVariable("INTEGRATION_PG_CONNECTION")
            ?? "Host=localhost;Database=designtime_placeholder;Username=postgres;Password=postgres";
        o.UseNpgsql(conn);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Extension A: materialised view on the entity.
        b.Entity<Product>().HasAnnotation("MatView:Name", "ProductCount");
        b.Entity<Product>()
            .HasAnnotation("MatView:Query", "SELECT COUNT(*) AS \"Count\" FROM \"Products\"");

        // Extension B: GIN index on Name property.
        b.Entity<Product>().Property(p => p.Name).HasAnnotation("PgIndex:Gin", true);
    }
}
