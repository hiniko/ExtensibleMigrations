using Microsoft.EntityFrameworkCore;

namespace MaterializedViewSample;

public sealed class Order
{
    public int Id { get; set; }
    public string Customer { get; set; } = "";
    public decimal Total { get; set; }
}

/// <summary>
/// Projection type for the materialised view. Declaring the view via
/// <c>HasMaterializedView&lt;OrderTotal&gt;</c> registers it as a keyless
/// entity, so it lands in the EF snapshot as a typed entry and can be
/// queried via <c>context.OrderTotals.ToList()</c>.
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

    protected override void OnConfiguring(DbContextOptionsBuilder o) =>
        o.UseSqlite("DataSource=orders.db");

    protected override void OnModelCreating(ModelBuilder b) =>
        b.HasMaterializedView<OrderTotal>(
            "OrderTotalsByCustomer",
            "SELECT Customer, SUM(Total) AS Total FROM Orders GROUP BY Customer"
        );
}
