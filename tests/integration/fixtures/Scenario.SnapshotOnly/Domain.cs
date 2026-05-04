using Microsoft.EntityFrameworkCore;

namespace Scenario.SnapshotOnly;

public sealed class Widget
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class WidgetContext : DbContext
{
    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void OnConfiguring(DbContextOptionsBuilder o)
    {
        var conn =
            Environment.GetEnvironmentVariable("INTEGRATION_PG_CONNECTION")
            ?? "Host=localhost;Database=designtime_placeholder;Username=postgres;Password=postgres";
        o.UseNpgsql(conn);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Annotation consumed only by a snapshot handler — no migration
        // operation is emitted, but the snapshot must record it.
        b.Entity<Widget>().HasAnnotation("Meta:Owner", "team-platform");
    }
}
