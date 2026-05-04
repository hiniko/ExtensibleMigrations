# Examples rework — design spec

**Date:** 2026-04-29
**Topic:** Restructure `docs/examples.md` from inline catalogue into per-file worked examples, replacing low-signal examples with three new ones that exercise distinct mechanisms.
**Status:** Draft awaiting user review.

## Motivation

The current `docs/examples.md` is a single file with four inline examples. Three of them (materialised view, BeforeCore Postgres extension, snapshot-only metadata) read as mechanical demonstrations of plumbing rather than uses a reader would actually want to copy into a real project. Two of those (materialised view, Postgres extension) are also weakened by Tier 1 native EF / Npgsql APIs being preferred — the docs themselves say so.

The first example (attribute-driven full-text index) is the one we keep. It is also currently labelled "the recommended pattern", which over-reaches: it is *one* well-fleshed-out pattern, not a prescription consumers should follow regardless of fit.

This rework:
- Splits each example into its own doc file, runnable sample, and integration fixture (three artefacts per example).
- Replaces the three weak examples with three new ones that demonstrate distinct, real-world mechanisms.
- Drops the "recommended" framing on example 1.
- Preserves the existing fixtures that still serve other docs (they are referenced, not promoted to top-level examples).

## Final lineup

| # | Title | Mechanism | New / kept |
|---|---|---|---|
| 1 | Attribute-driven full-text index | Property attribute → `ModelBuilder` extension → annotations → handler trio | Kept (refactored, drop "recommended" framing) |
| 2 | Versioned stored procedures | `[Sproc(Version=N)]` over `.sql` files, multi-version coexist, manual `static class Sprocs` reference for composed SQL | New |
| 3 | SQL function from file | `.sql` file as source-of-truth, content-hash diff, regen on change | New |
| 4 | Tenant-scoped row-level security | `[TenantOwned(Column = "TenantId")]` → `CREATE POLICY` + `ENABLE ROW LEVEL SECURITY`, runtime context via `current_setting('app.tenant_id')` | New |

**Pruned from the examples doc** (fixtures stay, referenced from other docs):
- `Scenario.MaterializedView` — referenced from `docs/snapshot-completeness.md`.
- `Scenario.BeforeCorePhase` — referenced from `docs/architecture.md` (BeforeCore mechanism).
- `Scenario.SnapshotOnly` — referenced from a new section in `docs/handlers.md`.
- `Scenario.NativeIndexFromAttribute` — already only referenced from `docs/snapshot-completeness.md`. No change.
- `Scenario.MultiExtension`, `Scenario.ExplicitDI` — stay in the existing "more examples" table.

## File layout

```
docs/
  examples.md                            # index: short intro + table linking to each example
  examples/
    01-attribute-driven-fulltext.md
    02-versioned-sprocs.md
    03-sql-function-from-file.md
    04-tenant-scoped-rls.md
  handlers.md                            # gains a "Snapshot-only handlers" section

samples/
  README.md                              # one-line per sample, links to doc
  AttributeDrivenFullTextSample/         # new sample matching example 1
  VersionedSprocsSample/                 # new
  SqlFunctionFromFileSample/             # new
  TenantScopedRlsSample/                 # new
  MaterializedViewSample/                # DELETE (mat view no longer in lineup)

tests/integration/fixtures/
  Scenario.AttributeDriven/              # exists, keep
  Scenario.VersionedSprocs/              # new
  Scenario.SqlFunctionFromFile/          # new
  Scenario.TenantScopedRls/              # new
  # Untouched (referenced from other docs):
  #   Scenario.BeforeCorePhase
  #   Scenario.MaterializedView
  #   Scenario.MultiExtension
  #   Scenario.NativeIndexFromAttribute
  #   Scenario.SnapshotOnly
  #   Scenario.ExplicitDI
```

**Naming conventions:**
- Doc: `NN-kebab-case.md` with leading number for stable order.
- Sample: `<Concept>Sample/`, PascalCase, matches existing `MaterializedViewSample` precedent.
- Fixture: `Scenario.<Concept>/`, matches existing precedent.

**Sample vs fixture roles:**
- **Fixture** = minimal reproducible model that integration tests run against. Asserts scaffold output + apply roundtrip. Test-shaped.
- **Sample** = runnable `Program.cs` consumer using the concept end-to-end (model + DI + actual query). Demonstrates real DX.
- Doc snippets are pulled from the sample (more natural reading) and the doc points at the fixture for the integration assertions.

## Per-example doc structure

Each `docs/examples/NN-*.md` follows this skeleton. Sections are scaled to the concept, but the order and headings are stable so readers can navigate every example identically.

```markdown
# Example N — <title>

<one-paragraph what-and-why>

**Sample:** [`samples/<X>Sample`](../../samples/<X>Sample) — runnable.
**Fixture:** [`tests/integration/fixtures/Scenario.<X>`](../../tests/integration/fixtures/Scenario.<X>) — integration-tested.

## Consumer surface
<the model + attribute/extension call as a user writes them>

## Library pieces
### Attribute / ModelBuilder extension
### Operation type(s)
### Differ handler (IMigrationOperationHandler)
### C# codegen handler (ICSharpMigrationOperationHandler)
### Snapshot handler (IMigrationsSnapshotHandler)   # only when relevant

## Generated migration
<expected scaffold output>

## Snapshot shape
<key snapshot lines the framework added — only when typed snapshot entries are written>

## Variations            # only when relevant
<alternative source-of-truth shapes etc>

## See also
- links
```

**Rules:**
- Code blocks pulled from the sample/fixture, not invented inline. Keeps the doc honest — if the fixture compiles, the snippet works.
- "Generated migration" is real scaffold output, backed by a fixture assertion.
- "Snapshot shape" only included when the handler writes typed snapshot entries — Tier 1 native cases skip it.
- "Variations" only present where the example has alternative shapes (example 2 is the only one that does — inline-string body and static-strings-class body).

**`examples.md` root** shrinks to:
- One-paragraph intro to the framework's concept of "worked example".
- Table: Example | Concept | Mechanism | Doc link | Fixture link.
- Pointers to `architecture.md`, `handlers.md`, `snapshot-completeness.md`.

## Per-example design details

### Example 1 — Attribute-driven full-text index

**Status:** refactor of existing inline content + existing `Scenario.AttributeDriven` fixture.

**Changes:**
- Split inline content out of `examples.md` into `docs/examples/01-attribute-driven-fulltext.md`.
- Drop "the **recommended pattern**" framing. Reframe as "one fully-worked pattern showing the framework's full handler trio in use".
- Create new `samples/AttributeDrivenFullTextSample/` runnable consumer (the existing fixture is test-shaped, not a runnable program).
- No code changes to `Scenario.AttributeDriven` fixture or its handlers.

### Example 2 — Versioned stored procedures

**Status:** new. Pure additive.

**Semantics:**
- Multi-version coexist. All declared `[Sproc(Version=N)]` versions exist in the database simultaneously. Postgres function names: `<SprocName>_v<N>`, where `SprocName` is the consumer class name (or an explicit `[Sproc(Name = "...", Version=N)]` override).
- A version is dropped only when its declaration is removed from code. Caller picks version by name in composed SQL.
- No deprecation mechanism in v1 — left as a future optional follow-up (`Deprecates = N` attribute argument).

**Body source (canonical):** one `.sql` file per version, located beside the consumer's class. Filename encodes name + version: `Sprocs/GetActiveOrders.v1.sql`, `Sprocs/GetActiveOrders.v2.sql`. Standard Postgres function definitions including `CREATE OR REPLACE FUNCTION <name>_v<N>(...) RETURNS ... AS $$ ... $$`.

**Body source (variations doc'd, not in fixture):**
- Inline string in attribute: `[Sproc(Version=2, Body="...")]`.
- Static class of strings: `static class GetActiveOrdersBodies { public const string V1 = "..."; }` referenced from attribute.

**Consumer reference for composed SQL:**
- Hand-rolled `static class Sprocs { public const string GetActiveOrders_v2 = "GetActiveOrders_v2"; }` in consumer code.
- Used as: `db.Database.SqlQuery<Order>($"SELECT * FROM {Sprocs.GetActiveOrders_v2}({customerId})")`.
- No source generator. (A future companion package could provide one — mention as aside in the doc, do not build.)

**Mechanism:**
- `[Sproc(Version=N)]` attribute on consumer class. `ModelBuilder` extension scans for it via reflection, registers each version as an annotation on the model: `Sproc:<Name>:v<N>` → file path or body string.
- `IMigrationOperationHandler` diffs registered versions: target − source = creates, source − target = drops.
- `CreateSprocOperation { Name, Version, Body }` and `DropSprocOperation { Name, Version }`.
- `ICSharpMigrationOperationHandler` emits `migrationBuilder.Sql("CREATE OR REPLACE FUNCTION ...")` and `migrationBuilder.Sql("DROP FUNCTION ...")`.
- Phase: `AfterCore` for create (sprocs may reference tables), `BeforeCore` for drop (drop before tables they reference).
- `IMigrationsSnapshotHandler` re-emits the `Sproc:*` annotations via `b.HasAnnotation(...)` calls so the differ is stable across migrations.

**Integration assertions:**
- All declared versions present in `pg_proc` after apply.
- Removing a version from code → next migration emits `DROP FUNCTION` only for that version.
- Re-scaffold with no changes → empty `Up`/`Down`.
- Sample's `Program.cs` uses `Sprocs.GetActiveOrders_v2` constant in `SqlQuery` and returns expected rows.

### Example 3 — SQL function from file

**Status:** new. Pure additive.

**Semantics:**
- One canonical version per function. File contents are the source of truth. Renaming the file = renaming the function.
- Diff is content-hash based: snapshot stores `Func:<Name>:Hash`. Differ compares stored hash vs current file's hash. Mismatch → emit `CREATE OR REPLACE FUNCTION`. Equal → no migration.
- No version attribute (deliberately distinct from example 2 — different mechanism).

**Mechanism:**
- `[SqlFunction(File = "normalize_name.sql")]` attribute on consumer class. `ModelBuilder` extension reads the file, computes SHA-256 hash, registers `Func:<Name>:Body` and `Func:<Name>:Hash` annotations.
- `IMigrationOperationHandler` diffs hashes:
  - New name in target → `CreateSqlFunctionOperation`.
  - Removed name → `DropSqlFunctionOperation`.
  - Same name, different hash → `ReplaceSqlFunctionOperation`.
- `ICSharpMigrationOperationHandler` emits `CREATE OR REPLACE FUNCTION`, `DROP FUNCTION IF EXISTS`. Phase: `AfterCore` for create/replace, `BeforeCore` for drop.
- `IMigrationsSnapshotHandler` re-emits annotations.

**Integration assertions:**
- Function body in `pg_get_functiondef` matches the `.sql` file contents post-apply.
- Editing the `.sql` file → next migration emits `CREATE OR REPLACE FUNCTION`.
- No edit → empty migration.
- Renaming the file (and updating attribute) → drop + create.

### Example 4 — Tenant-scoped row-level security

**Status:** new. Pure additive.

**Semantics:**
- Entity-level attribute `[TenantOwned(Column = "TenantId")]` (default `Column = "TenantId"`, override per entity).
- Handler emits `ALTER TABLE ... ENABLE ROW LEVEL SECURITY` and a single `CREATE POLICY` per tenant-owned entity.
- Policy expression: `USING (<column> = current_setting('app.tenant_id')::uuid)`. Naive but standard. Doc covers what to swap for non-uuid tenant ids.
- Validates at handler time: `[TenantOwned]` on an entity whose model has no matching column → fail scaffold with a clear error.

**Runtime context (doc only, sample includes it):**
- App opens a connection, runs `SET app.tenant_id = '<value>'` per request via a `DbConnectionInterceptor`.
- Sample shows the interceptor + a DI registration. Doc explains rationale and trade-offs.

**Mechanism:**
- `ModelBuilder` extension scans for `[TenantOwned]`, sets `Rls:Enabled` and `Rls:Column` annotations on each entity type.
- `IMigrationOperationHandler` diffs: target − source = `EnableRlsOperation` + `CreatePolicyOperation`. source − target = `DropPolicyOperation` + `DisableRlsOperation`.
- `ICSharpMigrationOperationHandler`: `AfterCore` for create (policy depends on table existing), `BeforeCore` for drop.
- `IMigrationsSnapshotHandler` re-emits `Rls:*` annotations.

**Integration assertions:**
- Policy exists in `pg_policies` after apply.
- Connection without `app.tenant_id` set → SELECT returns 0 rows from a populated table.
- With `app.tenant_id` set → returns matching rows only.
- `Down` rolls back: policy gone, RLS disabled.

## Integration test pattern

Each new fixture uses the existing harness under `tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/`. No new test infrastructure is anticipated — the implementation plan will inventory the existing helpers (e.g. `EnsureScaffoldStable`, snapshot-shape assertion helpers used by `Scenario.MaterializedView`) and surface anything missing as plan steps.

Each new fixture gets:

**1. Scaffold assertion** — `dotnet ef migrations add Init` against the fixture, then assert:
- `Up()` body contains expected SQL/operations.
- `Down()` body inverse.
- Snapshot file contains the typed annotations the handler emitted.
- Re-running `migrations add Empty` produces empty `Up`/`Down` (differ-stable).

**2. Apply-roundtrip** (Docker required, gated as the existing fixtures are) — apply migration to a real Postgres instance, then assert per-example specifics (see each example section above). `Down` rolls back cleanly.

## Cross-cutting doc updates

- **`docs/handlers.md`** — add new section "Snapshot-only handlers" using `Scenario.SnapshotOnly` as the reference. Absorbs the dropped example 5.
- **`docs/snapshot-completeness.md`** — already references the kept fixtures. Verify links resolve after restructure. No content changes required.
- **`docs/architecture.md`** — already references `Scenario.BeforeCorePhase`. No changes.
- **`docs/examples.md` root** — fully rewritten as an index page (intro + table + pointers).
- **`samples/README.md`** — updated to list new samples and drop `MaterializedViewSample`.
- **`README.md`** — link to `docs/examples.md` already exists. No change.
- **`CHANGELOG.md`** — entry for the next release noting the examples reorganisation.

## Out of scope (future work)

- **Source generator companion package** for typed sproc/function references. Mentioned as an aside in example 2's doc, not built.
- **Sproc deprecation lifecycle** (`[Sproc(Version=2, Deprecates=1)]` shorthand). Layerable on top of multi-version coexistence later.
- **Non-Postgres providers** for any new example. All four target Postgres via Npgsql, matching the existing fixture set.
- **Soft-delete view, audit triggers, grants, partitioning, idempotent seeds** — considered during brainstorming, deferred. Saturate the same patterns the four chosen examples already cover.

## Risks and mitigations

- **Doc drift between sample, fixture, and code snippets.** Mitigated by: snippets pulled from the actual sample/fixture sources, integration test asserts the scaffold output (so a stale snippet implies a failing test or a deliberate update).
- **Sample projects accumulating maintenance debt.** Mitigated by: samples are minimal `Program.cs` consumers, not full apps. Each builds in CI alongside the main solution.
- **Test runtime increase.** Three new apply-roundtrip tests gated on Docker, same as existing. Not on the default `dotnet test` path. Mitigated by inheriting the existing gate.

## Approval

Pending user review of this spec before invoking the writing-plans skill.
