# Architecture

How EntityFrameworkCore.ExtensibleMigrations hooks into EF Core's design-time pipeline, and how to reason about handler ordering.

## The EF Core design-time pipeline

When you run `dotnet ef migrations add Foo`, EF Core does this at design time:

```
                +---------------------------+
                | Snapshot model (last)     |
                +-------------+-------------+
                              |
                              v
                +---------------------------+
                | DbContext model (current) |
                +-------------+-------------+
                              |
                              v   IMigrationsModelDiffer
                +---------------------------+
                | List<MigrationOperation>  |
                +-------------+-------------+
                              |
                              v   ICSharpMigrationOperationGenerator
                +---------------------------+
                | <Timestamp>_Foo.cs        |
                +---------------------------+

                +---------------------------+
                | DbContext model (current) |
                +-------------+-------------+
                              |
                              v   ICSharpSnapshotGenerator
                +---------------------------+
                | *ModelSnapshot.cs         |
                +---------------------------+
```

EF Core resolves `IDesignTimeServices` types it finds in the startup assembly + provider assembly + a few attribute-flagged extension points, in this order:

1. EF Core's own defaults are added first.
2. The database provider (e.g. Npgsql) overrides what it needs.
3. Types referenced via `[assembly: DesignTimeServicesReferenceAttribute]` run.
4. Types implementing `IDesignTimeServices` directly in the **startup project** assembly run **last**.

Step 4 is the only place where consumer code can reliably wrap services without being clobbered by the provider in step 2. That's where this package plugs in.

## How the package wires itself in

The package ships a `buildTransitive` MSBuild target that injects this tiny class into the consumer's compile output:

```csharp
internal sealed class _ExtensibleMigrationsAutoDesignTimeServices : IDesignTimeServices
{
    public void ConfigureDesignTimeServices(IServiceCollection services)
        => new ExtensibleMigrationsDesignTimeServices().ConfigureDesignTimeServices(services);
}
```

Because that class lives in the consumer's startup assembly, EF Core finds it at step 4 — *after* the database provider has registered its overrides. The forwarded `ExtensibleMigrationsDesignTimeServices` then wraps three services in place:

| Wrapped EF service | Wrapper added by this package | What it adds |
|---|---|---|
| `IMigrationsModelDiffer` | `ExtensibleMigrationsModelDiffer` | Calls registered `IMigrationOperationHandler`s before/after the default differ runs, so they can append custom `MigrationOperation` instances. |
| `ICSharpMigrationOperationGenerator` | `ExtensibleCSharpMigrationOperationGenerator` | Routes each operation to a registered `ICSharpMigrationOperationHandler` if one matches; partitions them into `BeforeCore` / `AfterCore` buckets and emits in that order around EF's default codegen. |
| `ICSharpSnapshotGenerator` | `ExtensibleCSharpSnapshotGenerator` | After the default snapshot is written, calls registered `IMigrationsSnapshotHandler`s so they can append additional state into the snapshot file. |

The wrappers preserve the original `ServiceDescriptor.Lifetime` (Singleton / Scoped / Transient) of whatever they replaced, so EF's own resolution semantics are unchanged.

## Handler discovery

By default the package scans every loaded assembly for types annotated with `[CustomMigrationHandler(Order = N)]` and registers them under the appropriate handler interface (transient lifetime, matched against the interface they implement).

You can also register handlers explicitly via:

```csharp
services.AddMigrationOperationHandler<MyDifferHandler>();
services.AddCSharpMigrationOperationHandler<MyCSharpHandler>();
services.AddMigrationsSnapshotHandler<MySnapshotHandler>();
services.AddExtensibleMigrationsFromAssembly(typeof(SomeMarker).Assembly);
```

To do that, write your own `IDesignTimeServices` class in the consumer project, forward to `ExtensibleMigrationsDesignTimeServices` first, then add your handlers:

```csharp
public sealed class MyDesignTimeServices : IDesignTimeServices
{
    public void ConfigureDesignTimeServices(IServiceCollection services)
    {
        new ExtensibleMigrationsDesignTimeServices().ConfigureDesignTimeServices(services);
        services.AddCSharpMigrationOperationHandler<MyHandler>();
    }
}
```

EF Core picks up the consumer's `IDesignTimeServices` and the auto-injected one is bypassed; the explicit registrations live alongside attribute-discovered ones in the same service collection.

## Phases (`OperationPhase`)

Each `ICSharpMigrationOperationHandler` declares `Phase(op)` per operation:

- **`BeforeCore`** — emit before EF Core's default operations (e.g. `CreateTable`, `DropTable`).
- **`AfterCore`** — emit after them.

The generator sorts ops into two buckets, runs the EF default in the middle, and emits the buckets around it:

```
emit beforeCore handler ops...
emit core EF ops (CreateTable, DropTable, ...)
emit afterCore handler ops...
```

### Choosing a phase

The phase is determined by the **dependency direction** between your operation and EF's tables:

- A materialised view depends on the underlying tables → create it **after** the tables (`AfterCore` for create), drop it **before** the tables are dropped (`BeforeCore` for drop).
- A Postgres extension is a prerequisite for tables that use it → create it **before** the tables (`BeforeCore` for create), drop it **after** the tables are gone (`AfterCore` for drop).

EF Core itself handles the Up/Down direction by passing operations in the correct order; your `Phase(op)` just has to encode the dependency relative to core ops, not the direction.

### Order

`[CustomMigrationHandler(Order = N)]` sorts the differ's `IMigrationOperationHandler` list — handlers with lower `Order` contribute their operations first, so their ops appear earlier in the input list passed to the C# generator. The C# generator preserves that order within each phase bucket. Use `Order` when two handlers both target the same phase and one must run before the other.

## File map

| File | Role |
|---|---|
| `ExtensibleMigrationsDesignTimeServices.cs` | EF-discovered entry point. Wraps services in `IServiceCollection`. |
| `ExtensibleMigrationsModelDiffer.cs` | `IMigrationsModelDiffer` wrapper. Delegates to `IMigrationOperationHandler`s. |
| `ExtensibleCSharpMigrationOperationGenerator.cs` | `ICSharpMigrationOperationGenerator` wrapper. Phase-bucket emit. |
| `ExtensibleCSharpSnapshotGenerator.cs` | `ICSharpSnapshotGenerator` wrapper. Delegates to `IMigrationsSnapshotHandler`s. |
| `IMigrationOperationHandler.cs` | Differ-side interface. |
| `ICSharpMigrationOperationHandler.cs` | C#-codegen interface. |
| `IMigrationsSnapshotHandler.cs` | Snapshot-codegen interface. |
| `OperationPhase.cs` | `BeforeCore` / `AfterCore`. |
| `CustomMigrationHandlerAttribute.cs` | Marker for attribute-based discovery. |
| `ServiceCollectionExtensions.cs` | Public DI helpers. |
| `buildTransitive/*.cs`, `*.targets` | MSBuild injection so EF picks up the wrap automatically. |

## See also

- [docs/handlers.md](handlers.md) — writing your own handler trio.
- [docs/examples.md](examples.md) — full input → output examples.
