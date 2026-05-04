# Contributing

Thanks for your interest. This is a small library — keep PRs focused.

## Build

```bash
dotnet build -c Release
```

## Test

```bash
dotnet test tests/EntityFrameworkCore.ExtensibleMigrations.Tests/
```

Unit + Sqlite end-to-end scaffolding tests. No external services needed.

## Integration tests

Integration tests run real migrations against a PostgreSQL container via Testcontainers. **Requires Docker running locally** — Docker absence is a test failure, not a skip.

```bash
dotnet tool restore
dotnet test tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/
```

## Compatibility matrix

`compat-matrix.json` lists the EF Core / Npgsql / `dotnet-ef` tool version triples the project commits to keeping green. CI fans each cell out into its own runner job (clean env per cell). Locally:

```bash
# Run the whole matrix on the host (mutates .config/dotnet-tools.json between
# cells; restored on exit).
bash scripts/run-compat-matrix.sh

# Run a single cell by name.
bash scripts/run-compat-matrix.sh --only ef-10.0.7

# Run each cell inside a fresh dotnet/sdk container (host docker socket
# forwarded so Postgres testcontainer still works).
bash scripts/run-compat-matrix.sh --docker
```

Adding a new cell: append an entry to `compat-matrix.json` with `name`, `efCoreVersion`, `npgsqlEfCoreVersion`, `dotnetEfToolVersion`, and `dotnetSdkVersion`. CI picks it up automatically — no YAML edits.

EF 10.0.0 / 10.0.1 are intentionally excluded: their transitive `System.Security.Cryptography.Xml 9.0.0` carries known-vulnerability advisories (`GHSA-37gx-xxp4-5rgx`, `GHSA-w3x6-4m5h-cxqf`) and fails restore under `TreatWarningsAsErrors`. Floor is **10.0.5**.

### Integration test architecture

- Fixture projects under `tests/integration/fixtures/<scenario>/` are real .NET projects with their own `DbContext` + handlers.
- The harness copies a fixture to `$TMPDIR`, runs `dotnet ef migrations add` / `database update` as child processes, asserts generated content against checked-in golden files (SHA equality, line diff on mismatch).
- Roundtrip = scaffold → apply → re-scaffold and assert the migration body is empty.
- Fixtures get the package's `buildTransitive` `<Compile>` injection automatically (the harness imports the targets file in the fixture's `Directory.Build.props`), so no forwarding `IDesignTimeServices` shim is needed in fixtures or in real consumers.

### Adding a new scenario

1. Create `tests/integration/fixtures/Scenario.YourName/` mirroring `Scenario.MaterializedView`.
2. Add a corresponding `Scenarios/YourNameTests.cs` in the integration test project.
3. First test run writes the golden under `tests/integration/golden/Scenario.YourName/` and fails — inspect it, then re-run.
4. Once green, commit the goldens.

## Style

- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` is on. CI fails on warnings.
- Public API additions need XML doc comments.
- Match existing patterns; .editorconfig pins formatting.

## PRs

- Run `dotnet test` locally before opening.
- One topic per PR. If your change touches handler discovery + the differ wrapper, that's two PRs unless the change is genuinely indivisible.
- Update CHANGELOG.md under `[Unreleased]`.
