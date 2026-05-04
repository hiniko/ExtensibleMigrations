# Samples

## MaterializedViewSample

Smallest end-to-end demo: a `HasMaterializedView<OrderTotal>` extension, an operation type, and the handler pair that emit `CREATE MATERIALIZED VIEW` / `DROP MATERIALIZED VIEW` around EF's `CreateTable` / `DropTable`.

```bash
cd samples/MaterializedViewSample
dotnet ef migrations add Init
```

Inspect `Migrations/<timestamp>_Init.cs`:
- `Up()` ends with `migrationBuilder.Sql("CREATE MATERIALIZED VIEW ...");` after the `CreateTable` for `Orders`.
- `Down()` begins with `DROP MATERIALIZED VIEW` before `DropTable`.

The sample uses SQLite for design-time only — scaffolding is provider-agnostic. Don't `dotnet ef database update` it; SQLite has no `MATERIALIZED VIEW`. For an apply-and-roundtrip example see [`tests/integration/fixtures/Scenario.MaterializedView`](../tests/integration/fixtures/Scenario.MaterializedView), which targets Postgres and is exercised against a Testcontainers Postgres in CI.
