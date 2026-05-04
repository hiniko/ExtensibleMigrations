# Integration Test Rig Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an integration test harness that exercises ExtensibleMigrations end-to-end via the `dotnet ef` CLI against PostgreSQL containers, asserting both generated-file content (against golden files via SHA + diff) and roundtrip correctness (scaffold → apply → re-scaffold-must-be-empty), parameterised by EF Core version.

**Architecture:** Each test scenario is a real .NET project under `tests/integration/fixtures/<scenario>/` with its own DbContext + handlers. At test time the harness copies the fixture to a temp dir, runs `dotnet ef migrations add` / `database update` as child processes against a Testcontainers PostgreSQL instance, captures the generated `Migrations/*.cs` content, normalises out the EF-injected timestamp, hashes against a checked-in golden file's hash, and on mismatch emits a unified diff. A second invocation of `migrations add` after `database update` must produce "No changes detected" — this is the roundtrip assertion. EF Core version is parameterised via `Directory.Packages.props` central package management plus a `EfCoreVersion` MSBuild property; CI matrix sweeps the property.

**Tech Stack:** xUnit, Testcontainers.PostgreSql, Npgsql.EntityFrameworkCore.PostgreSQL, `dotnet-ef` as a local tool, Docker (CI runners + dev local).

---

## File structure overview

```
tests/integration/
├── EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/
│   ├── EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.csproj
│   ├── Harness/
│   │   ├── PostgresFixture.cs           # xUnit collection fixture, one container per assembly
│   │   ├── DotnetEfRunner.cs            # wraps Process.Start dotnet ef
│   │   ├── FixtureProject.cs            # copies fixture/ to tmp, manages lifecycle
│   │   ├── MigrationGoldenFile.cs       # normalize + SHA + diff
│   │   └── DotnetEfResult.cs            # exit code + stdout + stderr + parsed migration paths
│   ├── Scenarios/
│   │   ├── MaterializedViewTests.cs
│   │   ├── MultiExtensionTests.cs
│   │   └── RoundtripTests.cs
│   └── xunit.runner.json                # parallel = false (Postgres container shared)
├── fixtures/
│   ├── Scenario.MaterializedView/
│   │   ├── Scenario.MaterializedView.csproj
│   │   ├── Domain.cs                     # DbContext + entity
│   │   ├── Handlers.cs                   # operation handlers
│   │   └── Operations.cs                 # custom MigrationOperation types
│   └── Scenario.MultiExtension/
│       ├── Scenario.MultiExtension.csproj
│       ├── Domain.cs
│       ├── ViewExtension.cs              # one extension type
│       ├── IndexExtension.cs             # second extension type
│       └── ...
└── golden/
    ├── Scenario.MaterializedView/
    │   ├── Init.expected.cs              # checked-in expected migration body
    │   └── ModelSnapshot.expected.cs     # checked-in expected snapshot body
    └── Scenario.MultiExtension/
        └── ...
.config/
└── dotnet-tools.json                     # pins dotnet-ef version (local tool)
Directory.Packages.props                  # central package versions (EfCoreVersion, NpgsqlVersion)
```

Fixtures are kept *minimal* — they're not samples, they're test inputs. Each fixture exercises one or two specific behaviours.

---

## Task 1: Add Directory.Packages.props for centralised version control

EF Core version needs to be a single switchable knob. Central Package Management (CPM) is the right tool.

**Files:**
- Create: `Directory.Packages.props`
- Modify: `Directory.Build.props`
- Modify: `src/EntityFrameworkCore.ExtensibleMigrations/EntityFrameworkCore.ExtensibleMigrations.csproj`
- Modify: `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/EntityFrameworkCore.ExtensibleMigrations.Tests.csproj`
- Modify: `samples/MaterializedViewSample/MaterializedViewSample.csproj`

- [ ] **Step 1: Create Directory.Packages.props**

Create `/Users/sherman/projects/extensible-migrations/Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <EfCoreVersion Condition="'$(EfCoreVersion)' == ''">10.0.7</EfCoreVersion>
    <NpgsqlEfCoreVersion Condition="'$(NpgsqlEfCoreVersion)' == ''">10.0.0</NpgsqlEfCoreVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="$(EfCoreVersion)" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="$(EfCoreVersion)" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Relational" Version="$(EfCoreVersion)" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.InMemory" Version="$(EfCoreVersion)" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Sqlite" Version="$(EfCoreVersion)" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="$(NpgsqlEfCoreVersion)" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="$(EfCoreVersion)" />
    <PackageVersion Include="Testcontainers.PostgreSql" Version="4.0.0" />
    <PackageVersion Include="coverlet.collector" Version="6.0.4" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageVersion Include="xunit" Version="2.9.3" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Strip versions from existing csprojs**

In each csproj, remove `Version="..."` attributes from `<PackageReference>` elements. Just `<PackageReference Include="..." />` remains. Affected files:

`src/EntityFrameworkCore.ExtensibleMigrations/EntityFrameworkCore.ExtensibleMigrations.csproj`:
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.EntityFrameworkCore" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.Design" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" />
  <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
</ItemGroup>
```

`tests/EntityFrameworkCore.ExtensibleMigrations.Tests/EntityFrameworkCore.ExtensibleMigrations.Tests.csproj`:
```xml
<ItemGroup>
  <PackageReference Include="coverlet.collector" />
  <PackageReference Include="Microsoft.NET.Test.Sdk" />
  <PackageReference Include="xunit" />
  <PackageReference Include="xunit.runner.visualstudio" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.Design" />
</ItemGroup>
```

`samples/MaterializedViewSample/MaterializedViewSample.csproj`:
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.Design" />
</ItemGroup>
```

- [ ] **Step 3: Build everything to verify**

Run: `cd /Users/sherman/projects/extensible-migrations && dotnet restore && dotnet build -c Release 2>&1 | tail -10`
Expected: succeeds.

Run: `dotnet test --no-build -c Release 2>&1 | tail -3`
Expected: 17 pass.

Verify EF version override works:
```bash
dotnet build -c Release -p:EfCoreVersion=10.0.5 2>&1 | tail -5
```
Expected: succeeds (downgrades). Then restore back to default.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "build: adopt central package management for EF version sweeping

EfCoreVersion MSBuild property gates all EF package versions in one place.
Defaults to 10.0.7. Override via -p:EfCoreVersion=X.Y.Z. Enables CI matrix
sweeps across EF versions for the integration test rig."
```

---

## Task 2: Add dotnet-ef as local tool

The integration tests will shell out to `dotnet ef`. Pin the version via local tool manifest so all dev machines + CI agree.

**Files:**
- Create: `.config/dotnet-tools.json`

- [ ] **Step 1: Initialise local tool manifest**

```bash
cd /Users/sherman/projects/extensible-migrations
dotnet new tool-manifest
dotnet tool install dotnet-ef --version 10.0.7
```

This creates `.config/dotnet-tools.json` with `dotnet-ef` pinned.

- [ ] **Step 2: Verify tool works**

```bash
dotnet tool restore
dotnet ef --version
```
Expected: `Entity Framework Core .NET Command-line Tools 10.0.7`.

- [ ] **Step 3: Commit**

```bash
git add .config/dotnet-tools.json
git commit -m "build: pin dotnet-ef as local tool

Integration tests shell out to dotnet ef; pin the version so all dev
machines + CI use the same one. Run 'dotnet tool restore' to install."
```

---

## Task 3: Create integration test project skeleton

**Files:**
- Create: `tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.csproj`
- Create: `tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/xunit.runner.json`
- Modify: `ExtensibleMigrations.slnx`

- [ ] **Step 1: Create csproj**

Create `/Users/sherman/projects/extensible-migrations/tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Testcontainers.PostgreSql" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

Note: this project does NOT reference the ExtensibleMigrations source. It only orchestrates `dotnet ef` against fixture projects on disk.

- [ ] **Step 2: Disable test parallelism**

Create `/Users/sherman/projects/extensible-migrations/tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/xunit.runner.json`:

```json
{
  "$schema": "https://xunit.net/schema/current/xunit.runner.schema.json",
  "parallelizeAssembly": false,
  "parallelizeTestCollections": false
}
```

Postgres container is shared via collection fixture; serial tests avoid DB-name collisions.

- [ ] **Step 3: Add to solution**

Update `/Users/sherman/projects/extensible-migrations/ExtensibleMigrations.slnx` to add the new project to a `/tests/` folder entry. Final form:

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/EntityFrameworkCore.ExtensibleMigrations/EntityFrameworkCore.ExtensibleMigrations.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/EntityFrameworkCore.ExtensibleMigrations.Tests/EntityFrameworkCore.ExtensibleMigrations.Tests.csproj" />
    <Project Path="tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.csproj" />
  </Folder>
  <Folder Name="/samples/">
    <Project Path="samples/MaterializedViewSample/MaterializedViewSample.csproj" />
  </Folder>
</Solution>
```

- [ ] **Step 4: Build**

Run: `cd /Users/sherman/projects/extensible-migrations && dotnet restore && dotnet build -c Release 2>&1 | tail -5`
Expected: succeeds.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "test(integration): add empty integration test project skeleton

Out-of-process harness that drives dotnet ef against fixture projects
and asserts schema + content via a Postgres container. Parallel disabled
so collection fixture's container can be safely shared."
```

---

## Task 4: PostgresFixture — Testcontainers lifecycle

**Files:**
- Create: `tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/Harness/PostgresFixture.cs`

- [ ] **Step 1: Implement the fixture**

Create `tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/Harness/PostgresFixture.cs`:

```csharp
using Testcontainers.PostgreSql;

namespace EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Harness;

/// <summary>
/// xUnit collection fixture: spins up one Postgres 16 container for the test run.
/// Each test grabs a fresh database via <see cref="CreateDatabaseAsync"/>.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string AdminConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>
    /// Creates a new database with a unique name and returns its connection string.
    /// Caller is responsible for not leaking — DBs auto-clean when container disposes.
    /// </summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var dbName = $"em_{Guid.NewGuid():N}";
        await using var conn = new Npgsql.NpgsqlConnection(AdminConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
        await cmd.ExecuteNonQueryAsync();

        var builder = new Npgsql.NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = dbName };
        return builder.ConnectionString;
    }
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }
```

This requires the `Npgsql` package — Testcontainers.PostgreSql brings it transitively but be explicit. Add to Directory.Packages.props if missing: `<PackageVersion Include="Npgsql" Version="9.0.4" />` and reference in the test csproj.

- [ ] **Step 2: Add Npgsql to test csproj**

In `tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.csproj` add inside the existing `<ItemGroup>`:

```xml
<PackageReference Include="Npgsql" />
```

Add to `Directory.Packages.props` PackageVersion list:
```xml
<PackageVersion Include="Npgsql" Version="9.0.4" />
```

- [ ] **Step 3: Smoke test**

Create `tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/Harness/PostgresFixtureSmokeTests.cs`:

```csharp
namespace EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Harness;

[Collection(nameof(PostgresCollection))]
public class PostgresFixtureSmokeTests
{
    private readonly PostgresFixture _pg;
    public PostgresFixtureSmokeTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Container_starts_and_creates_unique_databases()
    {
        var c1 = await _pg.CreateDatabaseAsync();
        var c2 = await _pg.CreateDatabaseAsync();

        Assert.NotEqual(c1, c2);
        Assert.Contains("Database=em_", c1);
        Assert.Contains("Database=em_", c2);
    }
}
```

- [ ] **Step 4: Run**

Requires Docker running locally.

Run: `cd /Users/sherman/projects/extensible-migrations && dotnet test tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/ --no-build -c Release 2>&1 | tail -10`

Expected: 1 test passes (or skip with explanation if Docker not available).

If Docker isn't available locally during plan execution, that's a hard blocker — Testcontainers requires it. Report BLOCKED with "Docker daemon not reachable; integration tests cannot run locally without Docker Desktop / colima / podman-compatibility-shim."

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "test(integration): add Postgres Testcontainers fixture

One container per test assembly. Each test gets a unique database via
CreateDatabaseAsync. Serial test execution keeps things tidy."
```

---

## Task 5: DotnetEfRunner — child process wrapper

**Files:**
- Create: `tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/Harness/DotnetEfResult.cs`
- Create: `tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/Harness/DotnetEfRunner.cs`

- [ ] **Step 1: Result type**

Create `tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/Harness/DotnetEfResult.cs`:

```csharp
namespace EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Harness;

public sealed record DotnetEfResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Succeeded => ExitCode == 0;

    public string CombinedOutput => string.IsNullOrEmpty(StdErr)
        ? StdOut
        : $"{StdOut}\n--- STDERR ---\n{StdErr}";

    public void EnsureSuccess()
    {
        if (Succeeded) return;
        throw new InvalidOperationException(
            $"dotnet ef failed with exit code {ExitCode}.\n{CombinedOutput}");
    }
}
```

- [ ] **Step 2: Runner**

Create `tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/Harness/DotnetEfRunner.cs`:

```csharp
using System.Diagnostics;
using System.Text;

namespace EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Harness;

/// <summary>
/// Runs <c>dotnet ef</c> commands against a project directory.
/// </summary>
public sealed class DotnetEfRunner
{
    private readonly string _projectDir;
    private readonly IDictionary<string, string> _env;

    public DotnetEfRunner(string projectDir, IDictionary<string, string>? env = null)
    {
        _projectDir = projectDir;
        _env = env ?? new Dictionary<string, string>();
    }

    public Task<DotnetEfResult> AddMigrationAsync(string name, string? connectionString = null)
        => RunAsync($"ef migrations add {name} --project \"{_projectDir}\"", connectionString);

    public Task<DotnetEfResult> UpdateDatabaseAsync(string connectionString)
        => RunAsync($"ef database update --project \"{_projectDir}\"", connectionString);

    public Task<DotnetEfResult> RemoveLastMigrationAsync(string? connectionString = null)
        => RunAsync($"ef migrations remove --project \"{_projectDir}\" --force", connectionString);

    private async Task<DotnetEfResult> RunAsync(string args, string? connectionString)
    {
        var psi = new ProcessStartInfo("dotnet", args)
        {
            WorkingDirectory = _projectDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (connectionString is not null)
        {
            psi.EnvironmentVariables["INTEGRATION_PG_CONNECTION"] = connectionString;
        }
        foreach (var (k, v) in _env)
        {
            psi.EnvironmentVariables[k] = v;
        }

        using var p = Process.Start(psi)!;
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        await p.WaitForExitAsync();
        return new DotnetEfResult(p.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
```

`INTEGRATION_PG_CONNECTION` env var is the contract: fixture DbContexts read this to pick their connection.

- [ ] **Step 3: Smoke test**

Append to `tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/Harness/PostgresFixtureSmokeTests.cs`:

```csharp
    [Fact]
    public async Task DotnetEfRunner_reports_failure_on_invalid_args()
    {
        var dir = Path.GetTempPath();
        var runner = new DotnetEfRunner(dir);
        var result = await runner.AddMigrationAsync("X");

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.CombinedOutput);
    }
```

This expects `dotnet ef` to fail because the temp dir isn't a project — confirms the runner correctly captures failure exit codes.

- [ ] **Step 4: Run**

Run: `dotnet test tests/integration/... --no-build -c Release`
Expected: 2 tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "test(integration): DotnetEfRunner — child-process wrapper for dotnet ef

Wraps Process.Start with stdout/stderr capture. Tests pass connection
strings via INTEGRATION_PG_CONNECTION env var; fixture DbContexts read
the same."
```

---

## Task 6: FixtureProject — copy-to-temp lifecycle

**Files:**
- Create: `tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/Harness/FixtureProject.cs`

- [ ] **Step 1: Implement**

Create `tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/Harness/FixtureProject.cs`:

```csharp
namespace EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Harness;

/// <summary>
/// Copies a fixture project from <c>tests/integration/fixtures/&lt;name&gt;/</c>
/// to a temp directory and gives the test a clean working copy. Disposes by
/// deleting the temp dir.
/// </summary>
public sealed class FixtureProject : IDisposable
{
    public string ProjectDir { get; }
    public string Name { get; }

    private FixtureProject(string projectDir, string name)
    {
        ProjectDir = projectDir;
        Name = name;
    }

    public static FixtureProject Copy(string fixtureName)
    {
        var source = Path.Combine(RepoRoot(), "tests", "integration", "fixtures", fixtureName);
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"Fixture '{fixtureName}' not found at {source}");
        }

        var dest = Path.Combine(Path.GetTempPath(), $"em-it-{fixtureName}-{Guid.NewGuid():N}");
        CopyDirectory(source, dest);
        return new FixtureProject(dest, fixtureName);
    }

    public string ReadGenerated(string relativePath)
        => File.ReadAllText(Path.Combine(ProjectDir, relativePath));

    public string[] ListMigrationFiles()
    {
        var migrationsDir = Path.Combine(ProjectDir, "Migrations");
        if (!Directory.Exists(migrationsDir)) return Array.Empty<string>();
        return Directory.GetFiles(migrationsDir, "*.cs");
    }

    public void Dispose()
    {
        try { Directory.Delete(ProjectDir, recursive: true); }
        catch { /* test cleanup, swallow */ }
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "ExtensibleMigrations.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException("Repo root (ExtensibleMigrations.slnx) not found");
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
        {
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)));
        }
        foreach (var dir in Directory.GetDirectories(src))
        {
            // skip bin/obj — restore happens in the temp copy
            var name = Path.GetFileName(dir);
            if (name is "bin" or "obj" or "Migrations") continue;
            CopyDirectory(dir, Path.Combine(dst, name));
        }
    }
}
```

The `Migrations` directory is intentionally NOT copied — fixtures keep golden migrations in `golden/`, not `fixtures/`. This forces a clean scaffold each run.

- [ ] **Step 2: Smoke test (will be exercised in Task 8)**

No standalone test for this — its behaviour is covered by Task 8's scenario test once a fixture exists.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "test(integration): FixtureProject copies fixture to tmp for clean scaffolds

Each test gets a fresh working copy under \$TMPDIR; bin/obj/Migrations
excluded so scaffold has nothing to base on. Disposed = tmp dir deleted."
```

---

## Task 7: MigrationGoldenFile — normalize + SHA + diff

**Files:**
- Create: `tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/Harness/MigrationGoldenFile.cs`

- [ ] **Step 1: Implement**

Create `tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/Harness/MigrationGoldenFile.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Harness;

/// <summary>
/// Compares an actual generated migration .cs file against a checked-in golden
/// expected file, after normalising out the EF-injected timestamp and CRLF
/// differences. SHA-256 for fast equality; on mismatch, throws with a unified
/// diff between actual and expected for inspection.
/// </summary>
public static class MigrationGoldenFile
{
    private static readonly Regex MigrationTimestampRegex = new(
        @"\[Migration\(""\d{14}_", RegexOptions.Compiled);

    private static readonly Regex MigrationFilenameRegex = new(
        @"^\d{14}_", RegexOptions.Compiled);

    public static void AssertMatches(string actualContent, string goldenPath)
    {
        if (!File.Exists(goldenPath))
        {
            // First run: write actual as the new golden. Engineer must commit + review.
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllText(goldenPath, Normalise(actualContent));
            throw new Xunit.Sdk.XunitException(
                $"Golden file did not exist; wrote {goldenPath}. Inspect, then commit if correct.");
        }

        var actualNormalised = Normalise(actualContent);
        var goldenNormalised = Normalise(File.ReadAllText(goldenPath));

        var actualHash = Sha256(actualNormalised);
        var goldenHash = Sha256(goldenNormalised);

        if (actualHash == goldenHash) return;

        // Mismatch: write actual to /tmp and emit a unified diff line-by-line.
        var actualOut = Path.Combine(Path.GetTempPath(), $"actual-{Path.GetFileName(goldenPath)}");
        File.WriteAllText(actualOut, actualNormalised);
        throw new Xunit.Sdk.XunitException(
            $"Golden mismatch.\n  Expected SHA: {goldenHash}\n  Actual SHA:   {actualHash}\n" +
            $"  Golden:  {goldenPath}\n  Actual:  {actualOut}\n" +
            $"--- diff ---\n{Diff(goldenNormalised, actualNormalised)}");
    }

    public static string Normalise(string content)
    {
        // Strip EF timestamp inside [Migration("20240101000000_Name")] -> [Migration("_Name")]
        var stripped = MigrationTimestampRegex.Replace(content, @"[Migration(""");
        // CRLF -> LF
        stripped = stripped.Replace("\r\n", "\n");
        // Trailing whitespace (line-by-line)
        var lines = stripped.Split('\n').Select(l => l.TrimEnd());
        return string.Join('\n', lines);
    }

    public static bool IsGeneratedMigrationFile(string fileName)
        => MigrationFilenameRegex.IsMatch(Path.GetFileName(fileName));

    private static string Sha256(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }

    private static string Diff(string expected, string actual)
    {
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        var sb = new StringBuilder();
        var max = Math.Max(e.Length, a.Length);
        for (var i = 0; i < max; i++)
        {
            var el = i < e.Length ? e[i] : "<EOF>";
            var al = i < a.Length ? a[i] : "<EOF>";
            if (el == al) continue;
            sb.AppendLine($"L{i + 1,4} - {el}");
            sb.AppendLine($"L{i + 1,4} + {al}");
        }
        return sb.ToString();
    }
}
```

The "first run writes a golden then fails" pattern is intentional — it forces human review of the first golden before it locks in.

- [ ] **Step 2: Unit-test the normaliser**

Create `tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/Harness/MigrationGoldenFileTests.cs`:

```csharp
namespace EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Harness;

public class MigrationGoldenFileTests
{
    [Fact]
    public void Normalise_strips_migration_timestamp()
    {
        var input = "[Migration(\"20240101120000_Init\")]\nclass Init { }";
        var result = MigrationGoldenFile.Normalise(input);
        Assert.Contains("[Migration(\"_Init\")]", result);
    }

    [Fact]
    public void Normalise_converts_crlf_to_lf()
    {
        var input = "line1\r\nline2\r\n";
        var result = MigrationGoldenFile.Normalise(input);
        Assert.Equal("line1\nline2\n", result);
    }

    [Fact]
    public void Normalise_trims_trailing_whitespace()
    {
        var input = "line1   \nline2\t\n";
        var result = MigrationGoldenFile.Normalise(input);
        Assert.Equal("line1\nline2\n", result);
    }

    [Fact]
    public void IsGeneratedMigrationFile_matches_timestamp_prefix()
    {
        Assert.True(MigrationGoldenFile.IsGeneratedMigrationFile("20240101120000_Init.cs"));
        Assert.False(MigrationGoldenFile.IsGeneratedMigrationFile("MyContextModelSnapshot.cs"));
        Assert.False(MigrationGoldenFile.IsGeneratedMigrationFile("readme.md"));
    }
}
```

- [ ] **Step 3: Run**

Run: `dotnet test tests/integration/... --no-build -c Release`
Expected: 4 normaliser tests pass, plus prior 2.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "test(integration): MigrationGoldenFile — normalise + SHA + diff harness

Strips EF timestamp + CRLF differences, hashes for O(1) equality. On
mismatch writes actual to TMPDIR and throws with line-by-line diff.
First run with no golden writes the actual as the new expected and
fails so the human commits a reviewed baseline."
```

---

## Task 8: First fixture — MaterializedView scenario

The fixture is a real .NET project living under `tests/integration/fixtures/Scenario.MaterializedView/`. It depends on the local ExtensibleMigrations build (via ProjectReference), Npgsql provider, and EF Core Design.

**Files:**
- Create: `tests/integration/fixtures/Scenario.MaterializedView/Scenario.MaterializedView.csproj`
- Create: `tests/integration/fixtures/Scenario.MaterializedView/Domain.cs`
- Create: `tests/integration/fixtures/Scenario.MaterializedView/Operations.cs`
- Create: `tests/integration/fixtures/Scenario.MaterializedView/Handlers.cs`
- Create: `tests/integration/fixtures/Scenario.MaterializedView/Program.cs`
- Modify: `ExtensibleMigrations.slnx` (do NOT add fixture as a solution project — it's intentionally orphan so the harness is the only thing that compiles it during a test)

Wait — fixture must build for `dotnet ef` to scaffold against it. Two options:
- (a) Add to .slnx so a normal `dotnet build` builds it. Pros: caught by CI. Cons: fixtures are noise in solution.
- (b) Leave out of .slnx; harness restores+builds the temp copy. Pros: clean solution. Cons: each test does a full restore — slower.

Pick (a) — add to a `/tests/fixtures/` solution folder. CI builds them; tests just need `dotnet ef` to find a built csproj.

- [ ] **Step 1: Create fixture csproj**

Create `tests/integration/fixtures/Scenario.MaterializedView/Scenario.MaterializedView.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\..\src\EntityFrameworkCore.ExtensibleMigrations\EntityFrameworkCore.ExtensibleMigrations.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create domain**

Create `tests/integration/fixtures/Scenario.MaterializedView/Domain.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Scenario.MaterializedView;

public sealed class Order
{
    public int Id { get; set; }
    public string Customer { get; set; } = "";
    public decimal Total { get; set; }
}

public sealed class OrderContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnConfiguring(DbContextOptionsBuilder o)
    {
        var conn = Environment.GetEnvironmentVariable("INTEGRATION_PG_CONNECTION")
            ?? "Host=localhost;Database=designtime_placeholder;Username=postgres;Password=postgres";
        o.UseNpgsql(conn);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Order>().HasAnnotation("MatView:Name", "OrderTotalsByCustomer");
        b.Entity<Order>().HasAnnotation("MatView:Query",
            "SELECT \"Customer\", SUM(\"Total\") AS \"Total\" FROM \"Orders\" GROUP BY \"Customer\"");
    }
}
```

The connection string falls back to a placeholder for design-time scaffolding (when `dotnet ef` runs against the project, it constructs an `OrderContext` to read the model — it does NOT connect to the DB during `migrations add`, only during `database update`).

- [ ] **Step 3: Create operations + handlers**

Create `tests/integration/fixtures/Scenario.MaterializedView/Operations.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Scenario.MaterializedView;

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

Create `tests/integration/fixtures/Scenario.MaterializedView/Handlers.cs`:

```csharp
using EntityFrameworkCore.ExtensibleMigrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Scenario.MaterializedView;

[CustomMigrationHandler(Order = 200)]
public sealed class MaterializedViewOperationHandler : IMigrationOperationHandler
{
    public bool HasDifferences(IRelationalModel? source, IRelationalModel? target, bool defaultHasDifferences)
        => Views(target).Except(Views(source)).Any() || Views(source).Except(Views(target)).Any();

    public IReadOnlyList<MigrationOperation> GetOperations(IRelationalModel? source, IRelationalModel? target, IReadOnlyList<MigrationOperation> existing)
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

[CustomMigrationHandler(Order = 200)]
public sealed class MaterializedViewCSharpHandler : ICSharpMigrationOperationHandler
{
    public bool CanHandle(MigrationOperation op) => op is CreateMaterializedViewOperation or DropMaterializedViewOperation;

    public OperationPhase Phase(MigrationOperation op) =>
        op is DropMaterializedViewOperation ? OperationPhase.BeforeCore : OperationPhase.AfterCore;

    public void Generate(MigrationOperation op, IndentedStringBuilder builder)
    {
        switch (op)
        {
            case CreateMaterializedViewOperation c:
                builder.AppendLine($"migrationBuilder.Sql(\"CREATE MATERIALIZED VIEW \\\"{c.ViewName}\\\" AS {c.Query};\");");
                break;
            case DropMaterializedViewOperation d:
                builder.AppendLine($"migrationBuilder.Sql(\"DROP MATERIALIZED VIEW IF EXISTS \\\"{d.ViewName}\\\";\");");
                break;
        }
    }
}

[CustomMigrationHandler(Order = 200)]
public sealed class MaterializedViewSnapshotHandler : IMigrationsSnapshotHandler
{
    public void GenerateSnapshot(IModel model, IndentedStringBuilder builder)
    {
        foreach (var et in model.GetEntityTypes())
        {
            var name = et.FindAnnotation("MatView:Name")?.Value as string;
            var query = et.FindAnnotation("MatView:Query")?.Value as string;
            if (name is null || query is null) continue;
            builder.AppendLine($"// MatView snapshot: {name}");
        }
    }
}
```

The snapshot handler is included to exercise the Task 9 (from prior plan) snapshot wrapper. It just emits a comment for now — the goal is that the comment ends up in the snapshot file and is visible in the golden.

- [ ] **Step 4: Create program entry point**

Create `tests/integration/fixtures/Scenario.MaterializedView/Program.cs`:

```csharp
// Required so the project is an executable (dotnet ef wants Exe or Library).
return 0;
```

- [ ] **Step 5: Add fixture to solution**

Update `ExtensibleMigrations.slnx`. Add a `/tests/fixtures/` folder containing the fixture project:

```xml
<Folder Name="/tests/fixtures/">
  <Project Path="tests/integration/fixtures/Scenario.MaterializedView/Scenario.MaterializedView.csproj" />
</Folder>
```

- [ ] **Step 6: Build to verify fixture compiles**

Run: `cd /Users/sherman/projects/extensible-migrations && dotnet build tests/integration/fixtures/Scenario.MaterializedView/ -c Release 2>&1 | tail -10`
Expected: succeeds.

- [ ] **Step 7: Smoke-test scaffold by hand (sanity)**

```bash
cd /Users/sherman/projects/extensible-migrations
export INTEGRATION_PG_CONNECTION="Host=localhost;Database=throwaway;Username=postgres;Password=postgres"
dotnet ef migrations add Init --project tests/integration/fixtures/Scenario.MaterializedView/
ls tests/integration/fixtures/Scenario.MaterializedView/Migrations/
```
Expected: a `Migrations/` directory appears with two files: `<timestamp>_Init.cs` and `OrderContextModelSnapshot.cs`. Inspect the `_Init.cs`: should contain `CREATE MATERIALIZED VIEW "OrderTotalsByCustomer" AS ...`. Then DELETE the `Migrations/` folder before committing — fixtures must not carry generated migrations.

```bash
rm -rf tests/integration/fixtures/Scenario.MaterializedView/Migrations
```

If scaffold fails because EF can't find handlers, the design-time-services scan isn't picking up the fixture assembly — investigate by adding `[assembly: DesignTimeServicesReference("...")]` to the fixture or registering handlers via a custom `IDesignTimeServices` in the fixture.

- [ ] **Step 8: Commit fixture (without generated Migrations/)**

```bash
git add tests/integration/fixtures/ ExtensibleMigrations.slnx
git commit -m "test(integration): add MaterializedView fixture project

Real EF Core project under tests/integration/fixtures/. Three handlers
exercising operation, csharp generation, and snapshot contribution.
Targets Postgres via Npgsql.EntityFrameworkCore.PostgreSQL."
```

---

## Task 9: First scenario test — golden + apply + roundtrip

**Files:**
- Create: `tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/Scenarios/MaterializedViewTests.cs`
- Create: `tests/integration/golden/Scenario.MaterializedView/Init.expected.cs` (will be produced by first test run)
- Create: `tests/integration/golden/Scenario.MaterializedView/OrderContextModelSnapshot.expected.cs` (same)

- [ ] **Step 1: Write the test**

Create `tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/Scenarios/MaterializedViewTests.cs`:

```csharp
using EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Harness;

namespace EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Scenarios;

[Collection(nameof(PostgresCollection))]
public class MaterializedViewTests
{
    private readonly PostgresFixture _pg;
    public MaterializedViewTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Scaffold_apply_roundtrip()
    {
        using var fixture = FixtureProject.Copy("Scenario.MaterializedView");
        var ef = new DotnetEfRunner(fixture.ProjectDir);
        var conn = await _pg.CreateDatabaseAsync();

        // 1. Scaffold the initial migration.
        (await ef.AddMigrationAsync("Init", conn)).EnsureSuccess();

        var migrationFile = fixture.ListMigrationFiles()
            .Single(f => MigrationGoldenFile.IsGeneratedMigrationFile(f));
        var snapshotFile = Path.Combine(fixture.ProjectDir, "Migrations", "OrderContextModelSnapshot.cs");

        // 2. Assert content matches golden.
        var goldenDir = Path.Combine(GoldenRoot(), "Scenario.MaterializedView");
        MigrationGoldenFile.AssertMatches(
            File.ReadAllText(migrationFile),
            Path.Combine(goldenDir, "Init.expected.cs"));
        MigrationGoldenFile.AssertMatches(
            File.ReadAllText(snapshotFile),
            Path.Combine(goldenDir, "OrderContextModelSnapshot.expected.cs"));

        // 3. Apply migration to a real database.
        (await ef.UpdateDatabaseAsync(conn)).EnsureSuccess();

        // 4. Roundtrip: re-scaffold, expect "No changes detected" or empty migration body.
        var second = await ef.AddMigrationAsync("Empty", conn);
        second.EnsureSuccess();
        Assert.Contains(
            "No changes detected",
            second.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GoldenRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "ExtensibleMigrations.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return Path.Combine(dir!, "tests", "integration", "golden");
    }
}
```

- [ ] **Step 2: First run — golden files don't exist yet, test will write + fail**

Run: `cd /Users/sherman/projects/extensible-migrations && dotnet test tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/ --no-build -c Release --filter MaterializedViewTests 2>&1 | tail -30`
Expected: FAILS with "Golden file did not exist; wrote ... Inspect, then commit if correct."

- [ ] **Step 3: Inspect the freshly-written golden files**

Open `tests/integration/golden/Scenario.MaterializedView/Init.expected.cs` and `OrderContextModelSnapshot.expected.cs`. Verify:
- `Init.expected.cs` contains a `CreateTable` for `Orders` followed (after EF core ops) by `migrationBuilder.Sql("CREATE MATERIALIZED VIEW \"OrderTotalsByCustomer\" AS ...")`.
- Down direction has `DROP MATERIALIZED VIEW IF EXISTS` BEFORE the `DropTable`.
- `OrderContextModelSnapshot.expected.cs` contains the snapshot handler's comment: `// MatView snapshot: OrderTotalsByCustomer`.

If anything looks wrong, fix the fixture handlers; delete the goldens; re-run; iterate until correct.

- [ ] **Step 4: Re-run to verify pass**

Run: `dotnet test tests/integration/... --filter MaterializedViewTests 2>&1 | tail -10`
Expected: 1 passed.

- [ ] **Step 5: Commit goldens**

```bash
git add tests/integration/golden/
git commit -m "test(integration): MaterializedView scenario test + checked-in golden

Scaffold-apply-roundtrip test against Postgres container. Golden files
checked in for human-reviewable migration content; SHA used for fast
equality, diff for human inspection on mismatch."
```

---

## Task 10: Multi-extension scenario — combine handlers

A second fixture combining two unrelated extensions to confirm they don't step on each other (operation ordering, snapshot annotation namespacing, etc.).

**Files:**
- Create: `tests/integration/fixtures/Scenario.MultiExtension/Scenario.MultiExtension.csproj`
- Create: `tests/integration/fixtures/Scenario.MultiExtension/Domain.cs`
- Create: `tests/integration/fixtures/Scenario.MultiExtension/Operations.cs`
- Create: `tests/integration/fixtures/Scenario.MultiExtension/Handlers.cs`
- Create: `tests/integration/fixtures/Scenario.MultiExtension/Program.cs`
- Create: `tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/Scenarios/MultiExtensionTests.cs`
- Modify: `ExtensibleMigrations.slnx`

- [ ] **Step 1: Create fixture csproj**

Mirror Task 8 Step 1 exactly, with `Scenario.MultiExtension` substituted.

- [ ] **Step 2: Create domain combining two annotation kinds**

Create `tests/integration/fixtures/Scenario.MultiExtension/Domain.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Scenario.MultiExtension;

public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Sku { get; set; } = "";
}

public sealed class ProductContext : DbContext
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnConfiguring(DbContextOptionsBuilder o)
    {
        var conn = Environment.GetEnvironmentVariable("INTEGRATION_PG_CONNECTION")
            ?? "Host=localhost;Database=designtime_placeholder;Username=postgres;Password=postgres";
        o.UseNpgsql(conn);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Extension A: materialised view on the entity.
        b.Entity<Product>().HasAnnotation("MatView:Name", "ProductCount");
        b.Entity<Product>().HasAnnotation("MatView:Query",
            "SELECT COUNT(*) AS \"Count\" FROM \"Products\"");

        // Extension B: GIN index on Name property.
        b.Entity<Product>().Property(p => p.Name).HasAnnotation("PgIndex:Gin", true);
    }
}
```

- [ ] **Step 3: Create operations + handlers for both extensions**

Create `tests/integration/fixtures/Scenario.MultiExtension/Operations.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Scenario.MultiExtension;

public sealed class CreateMaterializedViewOperation : MigrationOperation
{
    public string ViewName { get; init; } = "";
    public string Query { get; init; } = "";
}

public sealed class DropMaterializedViewOperation : MigrationOperation
{
    public string ViewName { get; init; } = "";
}

public sealed class CreateGinIndexOperation : MigrationOperation
{
    public string TableName { get; init; } = "";
    public string ColumnName { get; init; } = "";
    public string IndexName { get; init; } = "";
}

public sealed class DropGinIndexOperation : MigrationOperation
{
    public string IndexName { get; init; } = "";
}
```

Create `tests/integration/fixtures/Scenario.MultiExtension/Handlers.cs`:

```csharp
using EntityFrameworkCore.ExtensibleMigrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Scenario.MultiExtension;

[CustomMigrationHandler(Order = 200)]
public sealed class MatViewHandler : IMigrationOperationHandler
{
    public bool HasDifferences(IRelationalModel? s, IRelationalModel? t, bool d)
        => Views(t).Except(Views(s)).Any() || Views(s).Except(Views(t)).Any();

    public IReadOnlyList<MigrationOperation> GetOperations(IRelationalModel? s, IRelationalModel? t, IReadOnlyList<MigrationOperation> e)
    {
        var ops = new List<MigrationOperation>();
        foreach (var (n, q) in Views(t).Except(Views(s))) ops.Add(new CreateMaterializedViewOperation { ViewName = n, Query = q });
        foreach (var (n, _) in Views(s).Except(Views(t))) ops.Add(new DropMaterializedViewOperation { ViewName = n });
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

[CustomMigrationHandler(Order = 300)]
public sealed class GinIndexHandler : IMigrationOperationHandler
{
    public bool HasDifferences(IRelationalModel? s, IRelationalModel? t, bool d)
        => Indexes(t).Except(Indexes(s)).Any() || Indexes(s).Except(Indexes(t)).Any();

    public IReadOnlyList<MigrationOperation> GetOperations(IRelationalModel? s, IRelationalModel? t, IReadOnlyList<MigrationOperation> e)
    {
        var ops = new List<MigrationOperation>();
        foreach (var (table, col, idx) in Indexes(t).Except(Indexes(s)))
            ops.Add(new CreateGinIndexOperation { TableName = table, ColumnName = col, IndexName = idx });
        foreach (var (_, _, idx) in Indexes(s).Except(Indexes(t)))
            ops.Add(new DropGinIndexOperation { IndexName = idx });
        return ops;
    }

    private static IEnumerable<(string Table, string Col, string Idx)> Indexes(IRelationalModel? m)
    {
        if (m is null) yield break;
        foreach (var et in m.Model.GetEntityTypes())
        {
            var table = et.GetTableName();
            if (table is null) continue;
            foreach (var p in et.GetProperties())
            {
                if (p.FindAnnotation("PgIndex:Gin") is { Value: true })
                {
                    var col = p.GetColumnName();
                    yield return (table, col, $"ix_gin_{table}_{col}");
                }
            }
        }
    }
}

[CustomMigrationHandler(Order = 200)]
public sealed class MatViewCSharpHandler : ICSharpMigrationOperationHandler
{
    public bool CanHandle(MigrationOperation op) => op is CreateMaterializedViewOperation or DropMaterializedViewOperation;
    public OperationPhase Phase(MigrationOperation op) => op is DropMaterializedViewOperation ? OperationPhase.BeforeCore : OperationPhase.AfterCore;
    public void Generate(MigrationOperation op, IndentedStringBuilder b)
    {
        switch (op)
        {
            case CreateMaterializedViewOperation c:
                b.AppendLine($"migrationBuilder.Sql(\"CREATE MATERIALIZED VIEW \\\"{c.ViewName}\\\" AS {c.Query};\");"); break;
            case DropMaterializedViewOperation d:
                b.AppendLine($"migrationBuilder.Sql(\"DROP MATERIALIZED VIEW IF EXISTS \\\"{d.ViewName}\\\";\");"); break;
        }
    }
}

[CustomMigrationHandler(Order = 300)]
public sealed class GinIndexCSharpHandler : ICSharpMigrationOperationHandler
{
    public bool CanHandle(MigrationOperation op) => op is CreateGinIndexOperation or DropGinIndexOperation;
    public OperationPhase Phase(MigrationOperation op) => op is DropGinIndexOperation ? OperationPhase.BeforeCore : OperationPhase.AfterCore;
    public void Generate(MigrationOperation op, IndentedStringBuilder b)
    {
        switch (op)
        {
            case CreateGinIndexOperation c:
                b.AppendLine($"migrationBuilder.Sql(\"CREATE INDEX \\\"{c.IndexName}\\\" ON \\\"{c.TableName}\\\" USING gin (to_tsvector('english', \\\"{c.ColumnName}\\\"));\");"); break;
            case DropGinIndexOperation d:
                b.AppendLine($"migrationBuilder.Sql(\"DROP INDEX IF EXISTS \\\"{d.IndexName}\\\";\");"); break;
        }
    }
}
```

- [ ] **Step 4: Program entry**

Create `tests/integration/fixtures/Scenario.MultiExtension/Program.cs`:

```csharp
return 0;
```

- [ ] **Step 5: Add to solution**

Append to `ExtensibleMigrations.slnx`'s `/tests/fixtures/` folder:

```xml
<Project Path="tests/integration/fixtures/Scenario.MultiExtension/Scenario.MultiExtension.csproj" />
```

- [ ] **Step 6: Write the test**

Create `tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/Scenarios/MultiExtensionTests.cs`:

```csharp
using EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Harness;

namespace EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Scenarios;

[Collection(nameof(PostgresCollection))]
public class MultiExtensionTests
{
    private readonly PostgresFixture _pg;
    public MultiExtensionTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Two_handlers_emit_in_attribute_order_and_apply_cleanly()
    {
        using var fixture = FixtureProject.Copy("Scenario.MultiExtension");
        var ef = new DotnetEfRunner(fixture.ProjectDir);
        var conn = await _pg.CreateDatabaseAsync();

        (await ef.AddMigrationAsync("Init", conn)).EnsureSuccess();

        var migrationFile = fixture.ListMigrationFiles()
            .Single(f => MigrationGoldenFile.IsGeneratedMigrationFile(f));
        var migrationContent = File.ReadAllText(migrationFile);

        // MatViewHandler is Order=200, GinIndexHandler is Order=300.
        // Both emit in AfterCore phase; lower Order should appear first.
        var matViewIdx = migrationContent.IndexOf("CREATE MATERIALIZED VIEW", StringComparison.Ordinal);
        var ginIdx = migrationContent.IndexOf("CREATE INDEX", StringComparison.Ordinal);
        Assert.True(matViewIdx > 0 && ginIdx > 0, "Both ops should be emitted");
        Assert.True(matViewIdx < ginIdx, "MatView (Order=200) should precede GIN index (Order=300)");

        // Golden compare.
        var goldenDir = Path.Combine(GoldenRoot(), "Scenario.MultiExtension");
        MigrationGoldenFile.AssertMatches(migrationContent, Path.Combine(goldenDir, "Init.expected.cs"));

        // Apply + roundtrip.
        (await ef.UpdateDatabaseAsync(conn)).EnsureSuccess();
        var second = await ef.AddMigrationAsync("Empty", conn);
        Assert.Contains("No changes detected", second.CombinedOutput, StringComparison.OrdinalIgnoreCase);
    }

    private static string GoldenRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "ExtensibleMigrations.slnx")))
            dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir!, "tests", "integration", "golden");
    }
}
```

- [ ] **Step 7: First run produces goldens, then re-run passes**

```bash
cd /Users/sherman/projects/extensible-migrations
dotnet test tests/integration/... --filter MultiExtensionTests
```
First run: golden missing, written, FAIL. Inspect goldens. Re-run: PASS.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "test(integration): MultiExtension scenario verifies handler ordering

Two handlers (Order=200 mat view, Order=300 gin index) both in AfterCore
phase. Test asserts emission order matches handler Order. Apply+roundtrip
confirms both extensions cohabit cleanly under a real Postgres."
```

---

## Task 11: EF version matrix in CI

**Files:**
- Modify: `.github/workflows/ci.yml`

- [ ] **Step 1: Add a separate integration job with EF matrix**

Append to `.github/workflows/ci.yml` after the existing `build-and-test` job:

```yaml
  integration-tests:
    needs: build-and-test
    runs-on: ubuntu-latest
    strategy:
      fail-fast: false
      matrix:
        ef-version: ['10.0.7', '10.0.8']
    services: {}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet tool restore
      - run: dotnet restore -p:EfCoreVersion=${{ matrix.ef-version }}
      - run: dotnet build -c Release --no-restore -p:EfCoreVersion=${{ matrix.ef-version }}
      - run: dotnet test tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/ -c Release --no-build --logger "trx;LogFileName=integration-${{ matrix.ef-version }}.trx"
      - uses: actions/upload-artifact@v4
        if: always()
        with:
          name: integration-results-ef${{ matrix.ef-version }}
          path: '**/integration-*.trx'
```

Docker is preinstalled on `ubuntu-latest` GitHub runners, so Testcontainers works without any extra setup. Windows / macOS runners lack Docker by default — integration tests are Linux-only in CI.

The two versions in the matrix start the sweep. Add new EF versions (e.g. 10.0.x patch releases, future 11.x previews) by appending to the list.

- [ ] **Step 2: Document the EF version override**

Append to `CONTRIBUTING.md`:

````markdown

## Integration tests

Integration tests use Testcontainers + PostgreSQL. **Requires Docker running locally.**

```bash
dotnet tool restore
dotnet test tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/
```

To sweep EF Core versions:

```bash
dotnet test ... -p:EfCoreVersion=10.0.5
```

CI runs the integration suite against a small matrix of EF versions on every PR.
````

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "ci: integration test job with EF version matrix

New job runs the Testcontainers-backed scenario tests across an EF
matrix on Linux runners. Add new versions to the matrix list as they
release. CONTRIBUTING.md documents the local invocation + EfCoreVersion
override."
```

---

## Self-Review

**Spec coverage** (mapping back to the user's ask):
- "Test rig that runs examples on different package versions" → Tasks 1, 11 (CPM + matrix).
- "Run dotnet ef commands on test projects" → Tasks 5, 6, 8, 9, 10 (DotnetEfRunner + fixtures).
- "Postgres via Testcontainers" → Task 4 (PostgresFixture).
- "Roundtrip testing" → Tasks 9, 10 (`Empty` second migration + "No changes detected" assertion).
- "Expected vs output content of file, SHA, diff for inspection" → Task 7 (MigrationGoldenFile).
- "Test migration extensions and combinations to make sure they play nice" → Tasks 8 (single), 10 (multi).
- "Help with newer EF versions that might break things" → Task 11 (matrix).

**Placeholder scan:** None. All steps have concrete code or commands.

**Type consistency:**
- `DotnetEfResult` introduced Task 5, used Task 9, 10.
- `FixtureProject` Task 6, used Task 9, 10.
- `MigrationGoldenFile.AssertMatches` Task 7, used Task 9, 10.
- `PostgresFixture.CreateDatabaseAsync` Task 4, used Task 9, 10.
- `[CustomMigrationHandler(Order = N)]` consistent across fixtures.

Two open caveats flagged inline:
- Docker requirement for local + Linux-only CI is explicit (Tasks 4, 11).
- "Golden file does not exist on first run" pattern intentionally fails the first run so a human reviews the baseline (Task 7).

---

Plan complete and saved to `docs/superpowers/plans/2026-04-26-integration-test-rig.md`. Two execution options:

1. **Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** — Execute tasks in this session using executing-plans, batch with checkpoints.

Which approach?
