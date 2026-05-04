# EntityFrameworkCore.ExtensibleMigrations

An EF Core design-time extension framework for teaching the migration system about your own concepts, so they get diffed, scaffolded, and snapshotted like first-class citizens.

[![NuGet](https://img.shields.io/nuget/v/EntityFrameworkCore.ExtensibleMigrations.svg)](https://www.nuget.org/packages/EntityFrameworkCore.ExtensibleMigrations) [![CI](https://github.com/hiniko/extensible-migrations/actions/workflows/ci.yml/badge.svg)](https://github.com/hiniko/extensible-migrations/actions)

## Why

Define custom `MigrationOperation` types that participate in EF Core's design-time pipeline — diffed, scaffolded, and snapshotted alongside EF's built-ins.

Examples:
- Property/class attributes that express database features EF Core doesn't natively model — e.g. a `[FullText]` attribute that emits a Postgres GIN index, a `[MaterializedView]` annotation that emits `CREATE MATERIALIZED VIEW`.
- Always-on helper operations — emit `GRANT` statements when tables are created, rebuild indexes when their definition changes.
- Custom codegen / snapshot output for EF's own operations.

Handlers contribute snapshot output too, so the differ sees state from the previous run and skips generating empty migrations.

## Install

```bash
dotnet add package EntityFrameworkCore.ExtensibleMigrations
```

The package ships an MSBuild `buildTransitive` target that auto-wires its design-time services into your project's compile. No manual `IDesignTimeServices` registration is required.


## Documentation

- **[docs/architecture.md](docs/architecture.md)** — how the package wraps EF Core's design-time services, how `BeforeCore` / `AfterCore` phases work, how handlers compose.
- **[docs/handlers.md](docs/handlers.md)** — step-by-step guide to writing your own handler trio.
- **[docs/snapshot-completeness.md](docs/snapshot-completeness.md)** — making framework outputs land in the EF snapshot as typed entries (Tier 1 native EF, Tier 2 consumer-side helper APIs).
- **[docs/examples.md](docs/examples.md)** — end-to-end examples with the input model and the exact migration output each produces.
- **[CONTRIBUTING.md](CONTRIBUTING.md)** — building, running tests, the integration rig + compatibility matrix.
- **[CHANGELOG.md](CHANGELOG.md)**.

## Compatibility

- .NET 10
- EF Core 10.0.5+ (lower 10.0.x versions ship transitive packages with known CVEs that fail restore under `TreatWarningsAsErrors`; see [CONTRIBUTING.md](CONTRIBUTING.md#compatibility-matrix) for the tested matrix).
- Database: provider-agnostic. Examples + integration tests target PostgreSQL via Npgsql.

## Licence

MIT
