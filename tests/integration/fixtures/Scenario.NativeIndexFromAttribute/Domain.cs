using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Scenario.NativeIndexFromAttribute;

/// <summary>
/// Property-level marker — same shape as Scenario.AttributeDriven but the
/// translation layer goes straight to native EF Core APIs instead of custom
/// migration operations. EF Core records the index in its model and snapshot,
/// so <c>model.GetIndexes()</c> sees it AND the snapshot file shows it as a
/// real index entity (not just a property annotation).
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

    [FullText]
    public string Body { get; set; } = "";

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

    protected override void OnModelCreating(ModelBuilder b) => b.UseTrgmIndexAnnotations();
}

/// <summary>
/// Translation layer demonstrating the fully Tier-1 pattern: every concept
/// is expressed via native EF / Npgsql APIs, so the snapshot has a typed
/// entry for each.
///
///   - Trgm indexes → <c>HasIndex(...).HasMethod("gin").HasOperators("gin_trgm_ops")</c>
///     (Npgsql provider extension). Lands in <c>model.GetIndexes()</c> and in
///     the snapshot as a typed <c>b.HasIndex(...).HasMethod(...)</c> entry.
///   - The <c>pg_trgm</c> extension prerequisite → Npgsql's native
///     <c>modelBuilder.HasPostgresExtension("pg_trgm")</c>. Lands in the
///     snapshot as a typed extension entry; Npgsql's migrator emits the
///     <c>CREATE EXTENSION</c> automatically — no framework handler needed.
/// </summary>
public static class FullTextModelBuilderExtensions
{
    public static ModelBuilder UseTrgmIndexAnnotations(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var anyTrgm = false;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var entityBuilder = modelBuilder.Entity(entityType.ClrType);
            var tableName = entityType.GetTableName();
            if (tableName is null)
                continue;

            foreach (var property in entityType.GetProperties())
            {
                var clrProperty = entityType.ClrType.GetProperty(
                    property.Name,
                    BindingFlags.Public | BindingFlags.Instance
                );
                if (clrProperty is null)
                    continue;
                if (clrProperty.GetCustomAttribute<FullTextAttribute>() is null)
                    continue;

                anyTrgm = true;

                entityBuilder
                    .HasIndex(property.Name)
                    .HasDatabaseName($"ix_ft_{tableName}_{property.Name}")
                    .HasMethod("gin")
                    .HasOperators("gin_trgm_ops");
            }
        }

        if (anyTrgm)
        {
            // Npgsql's native typed extension API. Snapshot serialises this
            // as a first-class entry (not just an annotation), and Npgsql's
            // migrator emits CREATE EXTENSION before any dependent index.
            modelBuilder.HasPostgresExtension("pg_trgm");
        }

        return modelBuilder;
    }
}
