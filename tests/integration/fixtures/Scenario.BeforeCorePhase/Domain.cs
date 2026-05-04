using Microsoft.EntityFrameworkCore;

namespace Scenario.BeforeCorePhase;

public sealed class Document
{
    public int Id { get; set; }
    public string Body { get; set; } = "";
}

public sealed class DocumentContext : DbContext
{
    public DbSet<Document> Documents => Set<Document>();

    protected override void OnConfiguring(DbContextOptionsBuilder o)
    {
        var conn =
            Environment.GetEnvironmentVariable("INTEGRATION_PG_CONNECTION")
            ?? "Host=localhost;Database=designtime_placeholder;Username=postgres;Password=postgres";
        o.UseNpgsql(conn);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Demonstrates the framework's BeforeCore-phase mechanism via a
        // raw model-level annotation. Real Postgres consumers should prefer
        // Npgsql's native modelBuilder.HasPostgresExtension(...) which gives
        // typed snapshot entries — see docs/snapshot-completeness.md.
        b.HasAnnotation("Pg:Extensions", new[] { "unaccent" });
    }
}
