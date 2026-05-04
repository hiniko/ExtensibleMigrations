using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Scenario.AttributeDriven;

/// <summary>
/// Property-level marker. The handler interprets this as "build a Postgres
/// GIN index over a tsvector of this column".
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FullTextAttribute : Attribute
{
    public string Language { get; init; } = "english";
}

public sealed class Article
{
    public int Id { get; set; }

    [FullText]
    public string Title { get; set; } = "";

    [FullText(Language = "english")]
    public string Body { get; set; } = "";

    // Not searchable — the handler ignores it.
    public string Author { get; set; } = "";
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
        // Translate [FullText] attributes into compact EF annotations the
        // handler can read at design time. The actual SQL is built by the
        // handler — this call only carries primitives.
        b.UseFullTextAnnotations();
    }
}

/// <summary>
/// Bridge between the consumer's domain attribute and the migration
/// handler's annotation contract. Library authors typically ship one of
/// these per attribute they introduce.
/// </summary>
public static class FullTextModelBuilderExtensions
{
    public static ModelBuilder UseFullTextAnnotations(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var clrProperty = entityType.ClrType.GetProperty(
                    property.Name,
                    BindingFlags.Public | BindingFlags.Instance
                );
                if (clrProperty is null)
                    continue;

                var attr = clrProperty.GetCustomAttribute<FullTextAttribute>();
                if (attr is null)
                    continue;

                property.SetAnnotation("FullText:IsFullText", true);
                property.SetAnnotation("FullText:Language", attr.Language);
            }
        }
        return modelBuilder;
    }
}
