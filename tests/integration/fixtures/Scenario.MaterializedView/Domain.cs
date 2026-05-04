using Microsoft.EntityFrameworkCore;

namespace Scenario.MaterializedView;

public sealed class Order
{
    public int Id { get; set; }
    public string Customer { get; set; } = "";
    public decimal Total { get; set; }
}

/// <summary>
/// Projection type for the OrderTotalsByCustomer materialised view. Declaring
/// it via the fixture-local <c>HasMaterializedView</c> helper (see
/// <c>ModelBuilderExtensions.cs</c>) makes the view a typed entity in
/// <see cref="DbContext.Model"/> + the snapshot, so consumers can query it
/// (<c>context.OrderTotals.ToList()</c>) and snapshot-aware tooling can
/// discover it via <c>GetEntityTypes()</c>.
/// </summary>
public sealed class OrderTotal
{
    public string Customer { get; set; } = "";
    public decimal Total { get; set; }
}

public sealed class OrderContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderTotal> OrderTotals => Set<OrderTotal>();

    protected override void OnConfiguring(DbContextOptionsBuilder o)
    {
        var conn =
            Environment.GetEnvironmentVariable("INTEGRATION_PG_CONNECTION")
            ?? "Host=localhost;Database=designtime_placeholder;Username=postgres;Password=postgres";
        o.UseNpgsql(conn);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasMaterializedView<OrderTotal>(
            "OrderTotalsByCustomer",
            "SELECT \"Customer\", SUM(\"Total\") AS \"Total\" FROM \"Orders\" GROUP BY \"Customer\""
        );
    }
}
