using Microsoft.EntityFrameworkCore;

namespace Scenario.ExplicitDI;

public sealed class Article
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
}

public sealed class ArticleContext : DbContext
{
    public DbSet<Article> Articles => Set<Article>();

    protected override void OnConfiguring(DbContextOptionsBuilder o)
    {
        var conn =
            Environment.GetEnvironmentVariable("INTEGRATION_PG_CONNECTION")
            ?? "Host=localhost;Database=designtime_placeholder;Username=postgres;Password=postgres";
        o.UseNpgsql(conn);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Article>().HasAnnotation("Tagged:Marker", "explicit-di-trigger");
    }
}
