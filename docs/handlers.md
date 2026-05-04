# Writing handlers

A handler trio teaches EF Core to scaffold a database concept it doesn't natively understand. This guide walks through each piece in the order you'll write them.

Read [docs/architecture.md](architecture.md) first if you haven't — this guide assumes you understand the wrap points and phases.

## The three interfaces

Every custom concept has up to three handlers:

| Interface | Runs at | Purpose |
|---|---|---|
| `IMigrationOperationHandler` | Differ time | Compares the snapshot's relational model to the current model and emits `MigrationOperation` instances for differences EF Core itself wouldn't produce. |
| `ICSharpMigrationOperationHandler` | Scaffold time | Turns those operations into the C# code that lands in `<Timestamp>_Name.cs`. |
| `IMigrationsSnapshotHandler` | Snapshot time | Appends to `*ModelSnapshot.cs` so the next diff has state to compare against. |

You may not need all three. If your concept's state can live inside `IMigrationsSnapshotHandler` already (because it's stored as a model annotation that EF Core writes for you, for instance), you can skip the operation handler — see [`Scenario.SnapshotOnly`](../tests/integration/fixtures/Scenario.SnapshotOnly).

## Choosing how consumers express the concept

Before writing handlers, pick the dev-ex pattern. Two styles work; the framework supports both, but they target different use cases.

### Pattern A — attribute → ModelBuilder extension → annotation (recommended for non-trivial)

Library author ships:

1. A property/class-level **attribute** consumers put on their domain types.
2. A **`ModelBuilder` extension method** (`UseXAnnotations()`) that walks the model, reads attributes via reflection, and writes compact EF annotations carrying primitives only (booleans, ints, short strings).
3. The handler trio that reads those annotations.

```csharp
// Library:
[AttributeUsage(AttributeTargets.Property)]
public sealed class FullTextAttribute : Attribute { public string Language { get; init; } = "english"; }

public static class FullTextModelBuilderExtensions
{
    public static ModelBuilder UseFullTextAnnotations(this ModelBuilder b) { /* set property annotations */ }
}

// Consumer:
public sealed class Article
{
    [FullText] public string Title { get; set; } = "";
}
public sealed class ArticleContext : DbContext
{
    protected override void OnModelCreating(ModelBuilder b) => b.UseFullTextAnnotations();
}
```

**When to use this:**
- The concept applies to specific properties, columns, or entities (not the model as a whole).
- The handler needs to derive several values from the same annotation (e.g. table + column + index name).
- Consumers will use this in production codebases — putting `[FullText]` next to the property is much more discoverable than a string annotation buried in `OnModelCreating`.

**Why it's better than raw annotations for non-trivial cases:**
- The attribute lives on the domain object where the intent belongs.
- The operation type carries only primitives — no SQL blobs in annotation values, which avoid escaping nightmares and keep the snapshot file small.
- Adding a new annotated property is a one-line consumer change with no handler-side work.

Reference fixture: [`Scenario.AttributeDriven`](../tests/integration/fixtures/Scenario.AttributeDriven).

### Pattern B — raw `HasAnnotation` (fine for simple, low-cardinality cases)

Skip the attribute layer; consumers call `HasAnnotation` directly:

```csharp
b.Entity<Order>().HasAnnotation("MatView:Name", "OrderTotalsByCustomer");
b.HasAnnotation("Pg:Extensions", new[] { "unaccent" });
```

**When this is fine:**
- The concept is rare in the model (one or two materialised views, a model-wide config).
- The annotation value is small and not a SQL blob.
- You want to skip writing reflection plumbing for one-off use.

**Avoid this style when** the annotation value carries SQL — the SQL becomes a string buried in the consumer's `OnModelCreating`, escaping rules apply twice, and reading the model in code becomes unpleasant.

Reference fixtures: [`Scenario.MaterializedView`](../tests/integration/fixtures/Scenario.MaterializedView), [`Scenario.BeforeCorePhase`](../tests/integration/fixtures/Scenario.BeforeCorePhase).

---

The rest of this guide uses the materialised view example with raw annotations to keep snippets short. For an attribute-driven walkthrough — the recommended pattern for non-trivial cases — see [docs/examples.md § attribute-driven full-text index](examples.md#example-1--attribute-driven-full-text-index-recommended-pattern).

## Step 1 — define the model surface

Whichever pattern you pick, the design-time signal landing on the model is an **EF annotation**. The handler reads it back via `IRelationalModel.Model.FindAnnotation(...)`.

For raw-annotation style:

```csharp
b.Entity<Order>().HasAnnotation("MatView:Name", "OrderTotalsByCustomer");
```

For attribute-driven style, see Pattern A above — the `UseXAnnotations()` extension does the equivalent `SetAnnotation` calls based on attributes.

## Step 2 — define the `MigrationOperation`

Subclass `MigrationOperation` once per kind of change. Keep it a plain DTO:

```csharp
public sealed class CreateMaterializedViewOperation : MigrationOperation
{
    public string ViewName { get; init; } = "";
    public string Query { get; init; } = "";
}

public sealed class DropMaterializedViewOperation : MigrationOperation
{
    public string ViewName { get; init; } = "";
}
```

Use `init`-only properties. The operation should be immutable from the differ's perspective.

## Step 3 — write `IMigrationOperationHandler`

The differ asks two questions: "is there a difference?" and "what operations would close it?".

```csharp
[CustomMigrationHandler(Order = 200)]
public sealed class MaterializedViewOperationHandler : IMigrationOperationHandler
{
    public bool HasDifferences(
        IRelationalModel? source,
        IRelationalModel? target,
        bool defaultHasDifferences)
        => Views(target).Except(Views(source)).Any()
        || Views(source).Except(Views(target)).Any();

    public IReadOnlyList<MigrationOperation> GetOperations(
        IRelationalModel? source,
        IRelationalModel? target,
        IReadOnlyList<MigrationOperation> existing)
    {
        var ops = new List<MigrationOperation>();
        foreach (var (name, query) in Views(target).Except(Views(source)))
            ops.Add(new CreateMaterializedViewOperation { ViewName = name, Query = query });
        foreach (var (name, _) in Views(source).Except(Views(target)))
            ops.Add(new DropMaterializedViewOperation { ViewName = name });
        return ops;
    }

    private static IEnumerable<(string Name, string Query)> Views(IRelationalModel? m)
    {
        if (m is null) yield break;
        foreach (var et in m.Model.GetEntityTypes())
        {
            var n = et.FindAnnotation("MatView:Name")?.Value as string;
            var q = et.FindAnnotation("MatView:Query")?.Value as string;
            if (n is not null && q is not null) yield return (n, q);
        }
    }
}
```

Notes:

- `source` is the **last snapshot's** relational model (null when the project has no migrations yet). `target` is the **current** model.
- `defaultHasDifferences` is what EF Core's default differ would have returned. Honour it (`OR` your own check) so you don't accidentally suppress real diffs.
- `existing` is the list of operations EF's differ already produced. You usually return only your own operations; the framework concatenates.

## Step 4 — write `ICSharpMigrationOperationHandler`

Turn each operation into the C# that lands in the generated migration's `Up()` / `Down()` body:

```csharp
[CustomMigrationHandler(Order = 200)]
public sealed class MaterializedViewCSharpHandler : ICSharpMigrationOperationHandler
{
    public bool CanHandle(MigrationOperation op)
        => op is CreateMaterializedViewOperation or DropMaterializedViewOperation;

    public OperationPhase Phase(MigrationOperation op) =>
        op is DropMaterializedViewOperation ? OperationPhase.BeforeCore : OperationPhase.AfterCore;

    public void Generate(MigrationOperation op, IndentedStringBuilder builder)
    {
        switch (op)
        {
            case CreateMaterializedViewOperation c:
                var query = c.Query.Replace("\"", "\\\"");
                builder.AppendLine(
                    $"migrationBuilder.Sql(\"CREATE MATERIALIZED VIEW \\\"{c.ViewName}\\\" AS {query};\");");
                break;
            case DropMaterializedViewOperation d:
                builder.AppendLine(
                    $"migrationBuilder.Sql(\"DROP MATERIALIZED VIEW IF EXISTS \\\"{d.ViewName}\\\";\");");
                break;
        }
    }
}
```

Phase rule of thumb (see [architecture.md § Phases](architecture.md#phases-operationphase)):

- Concept *depends on* tables → create `AfterCore`, drop `BeforeCore`. (Mat views, derived indexes, grants.)
- Tables *depend on* concept → create `BeforeCore`, drop `AfterCore`. (Extensions, custom types, schemas.)

Quote-escape carefully — your `Generate` output is raw C# source, and the string you pass to `migrationBuilder.Sql(...)` is interpreted by the target database. Two layers of escaping.

## Step 5 — write `IMigrationsSnapshotHandler` (only when needed)

EF Core's default snapshot writer already serialises annotations attached to the model, entities, and properties. So if your `IMigrationOperationHandler` reads its state from such annotations, **you don't need a snapshot handler** — the next diff will see the same annotations and converge correctly.

Write an `IMigrationsSnapshotHandler` only when EF doesn't auto-serialise the state your differ depends on. Typical case: the state lives somewhere outside the EF model graph, or you want to emit explicit `b.HasAnnotation(...)` calls that re-apply config:

```csharp
[CustomMigrationHandler(Order = 200)]
public sealed class FooSnapshotHandler : IMigrationsSnapshotHandler
{
    public void GenerateSnapshot(IModel model, IndentedStringBuilder builder)
    {
        foreach (var et in model.GetEntityTypes())
        {
            if (et.FindAnnotation("Foo:State")?.Value is not string state) continue;
            builder.AppendLine($"modelBuilder.Entity(\"{et.Name}\").HasAnnotation(\"Foo:State\", \"{state}\");");
        }
    }
}
```

If state needs to survive into the next diff and EF won't carry it, skipping this step means your operation re-fires on every scaffold. The roundtrip integration tests (scaffold → apply → re-scaffold-empty) catch that.

See [`Scenario.SnapshotOnly`](../tests/integration/fixtures/Scenario.SnapshotOnly) for a snapshot-handler-only fixture.

## Step 6 — register

Two options.

**Attribute-based (zero ceremony)** — add `[CustomMigrationHandler(Order = N)]` to each handler. The framework auto-discovers them from any loaded assembly when EF runs at design time. This is what the snippets above do.

**Explicit DI** — drop the attribute, write a tiny `IDesignTimeServices` in your project:

```csharp
public sealed class MyDesignTimeServices : IDesignTimeServices
{
    public void ConfigureDesignTimeServices(IServiceCollection services)
    {
        new ExtensibleMigrationsDesignTimeServices().ConfigureDesignTimeServices(services);
        services.AddMigrationOperationHandler<MaterializedViewOperationHandler>();
        services.AddCSharpMigrationOperationHandler<MaterializedViewCSharpHandler>();
        services.AddMigrationsSnapshotHandler<MaterializedViewSnapshotHandler>();
    }
}
```

Use the explicit form if you need finer control — for example registering different handlers for different DbContexts, or constructor-injecting handler dependencies.

## Step 7 — verify with a scaffold

```bash
dotnet ef migrations add Init
```

Inspect the generated `<Timestamp>_Init.cs`. Re-run:

```bash
dotnet ef migrations add Empty
```

If the second scaffold's `Up()` and `Down()` bodies are empty, your snapshot handler is in sync. If not, the differ is still seeing a diff — fix `IMigrationOperationHandler.HasDifferences` or the snapshot serialiser.

## Anti-patterns

- **Mutating `existing`** in `GetOperations`. Treat it as read-only — return a fresh list of your own ops.
- **Putting SQL inside the operation type**. Keep the operation a DTO; let the C# handler decide how to render it. This is what makes the same operation reusable across providers.
- **Skipping the snapshot handler** when state matters. The differ runs every time; if state isn't in the snapshot, you'll re-emit on every migration.
- **Phase = whatever feels right**. Pick by dependency direction (see above), not by intuition about "create comes after, drop comes before".
- **Ignoring quote-escaping**. SQL with `"` characters that lands in a C# string literal needs `\"` escaping at the C# layer; the runtime SQL parser then sees `"` correctly.

## See also

- [docs/architecture.md](architecture.md) — service wrap points + phase ordering reference.
- [docs/snapshot-completeness.md](snapshot-completeness.md) — making your handler's outputs land as typed snapshot entries (often you can skip the handler entirely and use native EF).
- [docs/examples.md](examples.md) — full handler trios with model + expected output.
- [`tests/integration/fixtures/`](../tests/integration/fixtures/) — runnable fixtures used by the integration test suite.
