# Snapshot completeness

EF Core's `*ModelSnapshot.cs` is supposed to be the schema's source of truth: the snapshot plus `OnModelCreating` should reproduce the database. EntityFrameworkCore.ExtensibleMigrations' default pattern — emit migration ops from custom `MigrationOperation` types — works for the differ but can leave the snapshot **incomplete**: things created by the framework end up as opaque annotations rather than typed entries, so:

- `model.GetEntityTypes()` doesn't list materialised views.
- `model.GetIndexes()` doesn't return a custom-method index (e.g. GIN/GiST trgm).
- Schema-aware tooling reading the snapshot sees orphan annotations whose meaning it can't decode.

This page describes a three-tier strategy for closing that gap. The framework's own fixtures under `tests/integration/fixtures/` exercise each tier.

## Two tiers, in order of preference

### Tier 1 — Native EF where it exists

Some non-table concepts already have native EF Core abstractions. Use them. EF's default writer emits typed entries, the default differ generates the migration, and you get full snapshot completeness for free.

| Concept | Native API | Snapshot shape |
|---|---|---|
| Views, materialised views | `Entity<T>().HasNoKey().ToView("Name")` | Typed keyless entity entry, queryable via `DbSet<T>`. |
| Indexes (provider-specific) | `HasIndex(...).HasMethod("gin").HasOperators("gin_trgm_ops")` (Npgsql) | Typed `HasIndex` entry with method + operators. |
| Sequences | `HasSequence("Name")` | Typed sequence entry. |
| Functions | `HasDbFunction(...)` / `[DbFunction]` | Typed function metadata. |
| Computed columns | `Property(p).HasComputedColumnSql(...)` | Property-level annotation. |
| Check constraints | `ToTable(t => t.HasCheckConstraint(...))` | Typed constraint. |
| Postgres extensions | `modelBuilder.HasPostgresExtension("name")` (Npgsql) | Typed extension entry; Npgsql's migrator emits `CREATE EXTENSION` automatically. |

For trgm indexes specifically, `Scenario.NativeIndexFromAttribute` shows this end-to-end: a `[FullText]` attribute is translated into native `HasIndex().HasMethod("gin").HasOperators("gin_trgm_ops")` calls plus `HasPostgresExtension("pg_trgm")`. The snapshot then carries:

```csharp
b.HasIndex("Title")
    .HasDatabaseName("ix_ft_Articles_Title")
    .HasAnnotation("Npgsql:IndexMethod", "gin")
    .HasAnnotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
```

— a real index entity, returned by `model.GetIndexes()`, not just a property annotation.

### Tier 2 — First-class C# APIs over annotations (consumer-side)

When EF has no native abstraction, write a thin extension method in your own project that internally sets canonical annotations and (when relevant) calls into Tier 1 native EF for the parts EF *can* model. The C# API is readable; the underlying storage is annotations, which EF's default writer auto-serialises.

The framework deliberately does not ship these helpers — they're concept-specific, and shipping a one-size-fits-all `HasMaterializedView` would force naming + annotation conventions on consumers. The `Scenario.MaterializedView` fixture (`tests/integration/fixtures/Scenario.MaterializedView/ModelBuilderExtensions.cs`) shows the pattern in ~30 lines; copy + adapt it for your concept:

```csharp
public static ModelBuilder HasMaterializedView<TProjection>(
    this ModelBuilder modelBuilder, string viewName, string query,
    Action<EntityTypeBuilder<TProjection>>? configure = null)
    where TProjection : class
{
    modelBuilder.Entity<TProjection>(b =>
    {
        b.HasNoKey();
        b.ToView(viewName);                                  // Tier 1 — typed entity in snapshot
        b.HasAnnotation("MatView:Name", viewName);           // Tier 2 — handler-side state
        b.HasAnnotation("MatView:Query", query);
        configure?.Invoke(b);
    });
    return modelBuilder;
}
```

The fixture's snapshot shows the result:

```csharp
modelBuilder.Entity("Scenario.MaterializedView.OrderTotal", b =>
{
    b.Property<string>("Customer").IsRequired().HasColumnType("text");
    b.Property<decimal>("Total").HasColumnType("numeric");

    b.ToTable((string)null);
    b.ToView("OrderTotalsByCustomer", (string)null);

    b
        .HasAnnotation("MatView:Name", "OrderTotalsByCustomer")
        .HasAnnotation("MatView:Query", "SELECT \"Customer\", SUM(\"Total\") AS \"Total\" FROM \"Orders\" GROUP BY \"Customer\"");
});
```

`model.GetEntityTypes()` returns the view; `model.FindEntityType(typeof(OrderTotal)).GetViewName()` returns the name; tooling that knows the `MatView:*` annotation keys can read the management state directly.

If callers shouldn't have to know annotation keys, expose a typed read surface alongside the helper — a small `IModel` extension that walks the model and returns strongly-typed records. For Npgsql-managed concepts Npgsql already provides typed read APIs (`model.GetPostgresExtensions()` etc.); use those.

## Picking a tier

Walk this in order:

1. **Does EF Core already model the concept?** If yes, use native EF — Tier 1. (Materialised views via `ToView`, custom indexes via `HasIndex().HasMethod(...)`, Postgres extensions via `HasPostgresExtension`.)
2. **Does the concept also need management state EF can't carry natively?** Write a Tier 2 helper in your own project that stacks annotations on top of native EF (e.g. `HasMaterializedView` adds `MatView:Query` to a `ToView` keyless entity). The fixture under `Scenario.MaterializedView` is the reference template.
3. **Is it purely outside EF's vocabulary?** Use the framework's handler pipeline — define a `MigrationOperation` and write the operation/csharp handlers (and a snapshot handler if EF won't auto-serialise the state). EF's diff and apply steps still work correctly, but the snapshot entries are the least typed of the options.

## Ensuring it stays this way

Every refactored fixture grows snapshot-shape assertions in its integration test, so future EF version bumps can't silently strip the typed entries we depend on:

| Fixture | Snapshot assertion |
|---|---|
| `Scenario.MaterializedView` | `OrderTotal` entity is present, with `b.ToView("OrderTotalsByCustomer")`, `b.ToTable((string)null)`, and the `MatView:Query` annotation in its lambda. |
| `Scenario.NativeIndexFromAttribute` | Snapshot contains `HasIndex` + `HasMethod` + `gin_trgm_ops`; model-level `HasPostgresExtension("pg_trgm")` entry. |
| `Scenario.BeforeCorePhase` | Snapshot contains the `Pg:Extensions` annotation (raw-annotation pattern, demonstrating the framework's BeforeCore mechanism). |
| `Scenario.AttributeDriven` | Snapshot contains the per-property `FullText:IsFullText` annotations the framework's handler reads. |

The compatibility matrix (`scripts/run-compat-matrix.sh`) runs the full suite under each pinned EF Core version, so a regression in EF's snapshot writer between versions trips a test rather than silently shipping incomplete snapshots.

## See also

- [docs/architecture.md](architecture.md) — wrap points and phase model.
- [docs/handlers.md](handlers.md) — choosing how consumers express the concept.
- [docs/examples.md](examples.md) — worked examples for each tier.
