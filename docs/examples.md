# Worked examples

Four end-to-end handler trios. Each shows the model surface and the migration the framework scaffolds. Handler implementations live in the runnable fixtures under [`tests/integration/fixtures/`](../tests/integration/fixtures/) and are exercised by the integration test suite — read the fixture for the full handler code.

The first example is the **recommended pattern** for non-trivial cases: a property-level attribute, a `ModelBuilder` extension that translates it into compact annotations, and a handler trio that reads them. The remaining examples use raw `HasAnnotation` calls — fine for one-offs, less ergonomic at scale.

## Example 1 — Attribute-driven full-text index (recommended pattern)

A property-level `[FullText]` attribute marks columns that should get a Postgres GIN index over a `tsvector`. The attribute lives next to the domain property; library code translates attributes into compact EF annotations via a `ModelBuilder` extension, and the handler renders SQL at scaffold time.

### Model surface (consumer code)

```csharp
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
    protected override void OnModelCreating(ModelBuilder b) => b.UseFullTextAnnotations();
}
```

### Generated migration

`dotnet ef migrations add Init` produces:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.CreateTable(
        name: "Articles",
        columns: table => new
        {
            Id = table.Column<int>(type: "integer", nullable: false)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
            Title = table.Column<string>(type: "text", nullable: false),
            Body = table.Column<string>(type: "text", nullable: false),
            Author = table.Column<string>(type: "text", nullable: false)
        },
        constraints: table => { table.PrimaryKey("PK_Articles", x => x.Id); });

    migrationBuilder.Sql("CREATE INDEX \"ix_ft_Articles_Body\" ON \"Articles\" USING gin (to_tsvector('english', \"Body\"));");
    migrationBuilder.Sql("CREATE INDEX \"ix_ft_Articles_Title\" ON \"Articles\" USING gin (to_tsvector('english', \"Title\"));");
}
```

`Author` has no attribute, no index. Adding `[FullText]` to a new property is a one-line consumer change; the handler picks it up on the next scaffold.

Full fixture: [`Scenario.AttributeDriven`](../tests/integration/fixtures/Scenario.AttributeDriven) — attribute, `ModelBuilder` extension, operation type, and handler pair. A real-world version of this pattern ships in [`EntityFrameworkCore.PagedQuery.Migrations`](https://github.com/hiniko/paged-query) under the name `[Searchable]` (GiST `pg_trgm`).

---

## Example 2 — Materialised view (`HasMaterializedView` + `ToView`)

A materialised view depends on the underlying tables, so create **after**, drop **before**. The fixture combines a Tier 1 keyless `ToView` entity (typed snapshot entry, queryable via `DbSet<TProjection>`) with Tier 2 management annotations the framework's handler reads to emit `CREATE MATERIALIZED VIEW`. See [docs/snapshot-completeness.md](snapshot-completeness.md) for tier definitions.

### Model surface

```csharp
public sealed class Order
{
    public int Id { get; set; }
    public string Customer { get; set; } = "";
    public decimal Total { get; set; }
}

// Projection type for the materialised view; declaring the view via
// HasMaterializedView<OrderTotal>(...) makes it a typed entity in the snapshot.
public sealed class OrderTotal
{
    public string Customer { get; set; } = "";
    public decimal Total { get; set; }
}

public sealed class OrderContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderTotal> OrderTotals => Set<OrderTotal>();

    protected override void OnModelCreating(ModelBuilder b)
        => b.HasMaterializedView<OrderTotal>(
            "OrderTotalsByCustomer",
            "SELECT \"Customer\", SUM(\"Total\") AS \"Total\" FROM \"Orders\" GROUP BY \"Customer\"");
}
```

`HasMaterializedView<T>` is a consumer-side helper (~30 lines) — the framework deliberately doesn't ship it. Copy [`Scenario.MaterializedView/ModelBuilderExtensions.cs`](../tests/integration/fixtures/Scenario.MaterializedView/ModelBuilderExtensions.cs) and adapt for your own concept.

### Generated migration

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.CreateTable(/* Orders */);

    migrationBuilder.Sql("CREATE MATERIALIZED VIEW \"OrderTotalsByCustomer\" AS SELECT \"Customer\", SUM(\"Total\") AS \"Total\" FROM \"Orders\" GROUP BY \"Customer\";");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql("DROP MATERIALIZED VIEW IF EXISTS \"OrderTotalsByCustomer\";");

    migrationBuilder.DropTable(name: "Orders");
}
```

`CreateTable` → `CREATE MATERIALIZED VIEW` (AfterCore); on `Down`, `DROP MATERIALIZED VIEW` → `DropTable` (BeforeCore). Re-running `dotnet ef migrations add Empty` after applying produces a migration with empty `Up()` / `Down()` — the snapshot is in sync.

Full fixture: [`Scenario.MaterializedView`](../tests/integration/fixtures/Scenario.MaterializedView).

---

## Example 3 — Postgres extension (`BeforeCore` create / `AfterCore` drop)

A Postgres extension is a prerequisite for tables that use it — opposite dependency direction to the materialised view.

> **Prefer Npgsql's native API for Postgres consumers.** `modelBuilder.HasPostgresExtension("name")` records the extension as a typed snapshot entry and emits `CREATE EXTENSION` automatically. See `Scenario.NativeIndexFromAttribute`. This example demonstrates the framework's `BeforeCore` mechanism for cases where no native equivalent exists.

### Model surface

```csharp
public sealed class DocumentContext : DbContext
{
    public DbSet<Document> Documents => Set<Document>();

    protected override void OnModelCreating(ModelBuilder b)
        => b.HasAnnotation("Pg:Extensions", new[] { "unaccent" });
}
```

### Generated migration

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS \"unaccent\";");

    migrationBuilder.CreateTable(/* Documents */);
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropTable(name: "Documents");

    migrationBuilder.Sql("DROP EXTENSION IF EXISTS \"unaccent\";");
}
```

`CREATE EXTENSION` lands **before** `CreateTable`, and `DROP EXTENSION` lands **after** `DropTable` — inverted vs. the materialised view.

Full fixture: [`Scenario.BeforeCorePhase`](../tests/integration/fixtures/Scenario.BeforeCorePhase).

---

## Example 4 — Snapshot-only metadata

Record information in the snapshot without generating migration SQL — annotate an entity for governance, document a constraint, etc. Only `IMigrationsSnapshotHandler` is needed.

### Model surface

```csharp
public sealed class WidgetContext : DbContext
{
    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void OnModelCreating(ModelBuilder b)
        => b.Entity<Widget>().HasAnnotation("Meta:Owner", "team-platform");
}
```

### Handler

```csharp
[CustomMigrationHandler(Order = 100)]
public sealed class MetaOwnerSnapshotHandler : IMigrationsSnapshotHandler
{
    public void GenerateSnapshot(IModel model, IndentedStringBuilder builder)
    {
        foreach (var et in model.GetEntityTypes())
        {
            if (et.FindAnnotation("Meta:Owner")?.Value is not string owner) continue;
            builder.AppendLine($"// MetaOwner: {et.Name} -> {owner}");
        }
    }
}
```

The migration body for `Init` contains only EF's default `CreateTable` — no custom SQL, no operation handler. The snapshot file gains the line:

```csharp
// MetaOwner: Scenario.SnapshotOnly.Widget -> team-platform
```

Replace the comment with whatever C# you actually need — for example a `b.HasAnnotation(...)` call that re-applies state EF's default snapshot writer doesn't itself serialise.

Full fixture: [`Scenario.SnapshotOnly`](../tests/integration/fixtures/Scenario.SnapshotOnly).

---

## More fixtures

| Fixture | Demonstrates |
|---|---|
| `Scenario.AttributeDriven` | Recommended pattern: `[FullText]` attribute → ModelBuilder extension → annotations → handler trio. |
| `Scenario.NativeIndexFromAttribute` | Same `[FullText]` shape but routed through native EF / Npgsql APIs — fully Tier-1, no custom MigrationOperation. |
| `Scenario.MaterializedView` | AfterCore create / BeforeCore drop, golden-file migration. |
| `Scenario.MultiExtension` | Two handlers (`Order=200` + `Order=300`) emitting in declared order. |
| `Scenario.BeforeCorePhase` | BeforeCore on Up + AfterCore on Down (Postgres extension). |
| `Scenario.SnapshotOnly` | Snapshot handler in isolation (no operation, no codegen). |
| `Scenario.ExplicitDI` | Handlers without `[CustomMigrationHandler]`, registered via `AddCSharpMigrationOperationHandler<T>()`. |

Run locally: `dotnet test tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/` (Docker required for the apply-and-roundtrip cases).

## See also

- [docs/architecture.md](architecture.md) — wrap points and phase model.
- [docs/handlers.md](handlers.md) — step-by-step guide to writing handlers.
- [docs/snapshot-completeness.md](snapshot-completeness.md) — picking native EF over custom handlers.
