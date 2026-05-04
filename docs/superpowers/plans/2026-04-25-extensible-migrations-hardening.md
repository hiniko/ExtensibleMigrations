# ExtensibleMigrations Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harden `EntityFrameworkCore.ExtensibleMigrations` from a freshly-extracted backend module into a polished pre-1.0 OSS package — fix correctness bugs, close API gaps (snapshot model contribution), expand test coverage, and add OSS hygiene.

**Architecture:** Keep the wrap-don't-replace approach to EF Core's design-time services. Replace the brittle Down-detection heuristic with an explicit per-operation `OperationPhase`. Add a third wrapper for `ICSharpSnapshotGenerator` symmetric to the existing two. Move from static-cached reflection scanning to a hybrid model: explicit DI registration via extension methods + optional attribute discovery. Introduce TDD via xUnit tests targeting each wrapper in isolation, plus end-to-end scaffolding tests using EF Core's InMemory provider.

**Tech Stack:** .NET 10, EF Core 10.0.7, xUnit, Microsoft.EntityFrameworkCore.InMemory (new test dep), GitHub Actions.

**Source review reference:** see code review in conversation; this plan implements those findings. Critical issues C1-C4, important issues I1-I7, snapshot gap, OSS readiness checklist, test strategy.

---

## Phase 1 — Test infrastructure

Tests come first so every subsequent change can be driven TDD. The current test project lives in `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/` and only covers handler discovery.

### Task 1: Add InMemory provider + shared stub folder

**Files:**
- Modify: `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/EntityFrameworkCore.ExtensibleMigrations.Tests.csproj`
- Create: `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/Stubs/` (folder)
- Create: `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/Stubs/SpyOperation.cs`
- Create: `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/Stubs/SpyMigrationsModelDiffer.cs`
- Create: `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/Stubs/SpyCSharpMigrationOperationGenerator.cs`

- [ ] **Step 1: Add InMemory + Sqlite EF provider package refs**

Modify `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/EntityFrameworkCore.ExtensibleMigrations.Tests.csproj` ItemGroup of PackageReferences to include:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.0.7" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.7" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.7" />
```

- [ ] **Step 2: Create shared spy operation type**

Create `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/Stubs/SpyOperation.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace EntityFrameworkCore.ExtensibleMigrations.Tests.Stubs;

public sealed class SpyOperation : MigrationOperation
{
    public string Marker { get; init; } = "";
}

public sealed class DropSpyOperation : MigrationOperation
{
    public string Marker { get; init; } = "";
}
```

- [ ] **Step 3: Create spy differ**

Create `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/Stubs/SpyMigrationsModelDiffer.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace EntityFrameworkCore.ExtensibleMigrations.Tests.Stubs;

public sealed class SpyMigrationsModelDiffer : IMigrationsModelDiffer
{
    public List<string> Calls { get; } = new();
    public bool ReturnHasDifferences { get; set; }
    public IReadOnlyList<MigrationOperation> ReturnDifferences { get; set; } = Array.Empty<MigrationOperation>();

    public bool HasDifferences(IRelationalModel? source, IRelationalModel? target)
    {
        Calls.Add(nameof(HasDifferences));
        return ReturnHasDifferences;
    }

    public IReadOnlyList<MigrationOperation> GetDifferences(IRelationalModel? source, IRelationalModel? target)
    {
        Calls.Add(nameof(GetDifferences));
        return ReturnDifferences;
    }
}
```

- [ ] **Step 4: Create spy CSharp generator**

Create `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/Stubs/SpyCSharpMigrationOperationGenerator.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace EntityFrameworkCore.ExtensibleMigrations.Tests.Stubs;

public sealed class SpyCSharpMigrationOperationGenerator : ICSharpMigrationOperationGenerator
{
    public List<IReadOnlyList<MigrationOperation>> Calls { get; } = new();

    public void Generate(string builderName, IReadOnlyList<MigrationOperation> operations, IndentedStringBuilder builder)
    {
        Calls.Add(operations.ToList());
        foreach (var op in operations)
        {
            builder.AppendLine($"// CORE:{op.GetType().Name}");
        }
    }
}
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build tests/EntityFrameworkCore.ExtensibleMigrations.Tests/EntityFrameworkCore.ExtensibleMigrations.Tests.csproj -c Release`
Expected: build succeeds, no warnings.

- [ ] **Step 6: Commit**

```bash
git add tests/EntityFrameworkCore.ExtensibleMigrations.Tests/
git commit -m "test: add EF Core InMemory/Sqlite test deps and shared spy stubs

EndToEnd scaffolding tests need a real EF provider; differ/generator wrapper
tests need spies. Shared Stubs/ folder so future tests don't duplicate."
```

---

### Task 2: Add InternalsVisibleTo so tests can poke internals later

**Files:**
- Modify: `src/EntityFrameworkCore.ExtensibleMigrations/EntityFrameworkCore.ExtensibleMigrations.csproj`

- [ ] **Step 1: Add ItemGroup**

In `src/EntityFrameworkCore.ExtensibleMigrations/EntityFrameworkCore.ExtensibleMigrations.csproj` add after the existing ItemGroup:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="EntityFrameworkCore.ExtensibleMigrations.Tests" />
</ItemGroup>
```

- [ ] **Step 2: Build**

Run: `dotnet build -c Release`
Expected: succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/EntityFrameworkCore.ExtensibleMigrations/EntityFrameworkCore.ExtensibleMigrations.csproj
git commit -m "build: expose internals to test assembly

Subsequent tasks make HandlerDiscovery internal and add reset-cache hooks
the tests need to call."
```

---

## Phase 2 — Core correctness fixes

### Task 3: Robust handler discovery — survive ReflectionTypeLoadException and dynamic assemblies

**Critical issue C2.** Current `HandlerDiscovery.cs:39` calls `assembly.GetTypes()` raw — any assembly with missing transitive references throws and the whole framework fails.

**Files:**
- Modify: `src/EntityFrameworkCore.ExtensibleMigrations/HandlerDiscovery.cs`
- Create: `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/HandlerDiscoveryReflectionTests.cs`

- [ ] **Step 1: Write failing test — skip ReflectionTypeLoadException**

Create `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/HandlerDiscoveryReflectionTests.cs`:

```csharp
using System.Reflection;
using System.Reflection.Emit;
using EntityFrameworkCore.ExtensibleMigrations;
using Xunit;

namespace EntityFrameworkCore.ExtensibleMigrations.Tests;

public class HandlerDiscoveryReflectionTests
{
    [Fact]
    public void SafeGetTypes_returns_loadable_types_when_GetTypes_throws()
    {
        var partial = new Type?[] { typeof(string), null, typeof(int) };
        var ex = new ReflectionTypeLoadException(partial, new Exception?[] { null, new Exception(), null });
        var asm = new ThrowingAssembly(ex);

        var result = HandlerDiscovery.SafeGetTypes(asm).ToList();

        Assert.Equal(new[] { typeof(string), typeof(int) }, result);
    }

    [Fact]
    public void SafeGetTypes_returns_empty_when_assembly_is_dynamic()
    {
        var ab = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("TestDyn"), AssemblyBuilderAccess.Run);

        Assert.Empty(HandlerDiscovery.SafeGetTypes(ab));
    }

    [Fact]
    public void SafeGetTypes_returns_empty_when_GetTypes_throws_unexpected()
    {
        var asm = new ThrowingAssembly(new InvalidOperationException("boom"));
        Assert.Empty(HandlerDiscovery.SafeGetTypes(asm));
    }

    private sealed class ThrowingAssembly : Assembly
    {
        private readonly Exception _ex;
        public ThrowingAssembly(Exception ex) { _ex = ex; }
        public override Type[] GetTypes() => throw _ex;
        public override bool IsDynamic => false;
    }
}
```

- [ ] **Step 2: Run test, verify it fails**

Run: `dotnet test --filter HandlerDiscoveryReflectionTests`
Expected: FAIL — `SafeGetTypes` does not exist.

- [ ] **Step 3: Implement SafeGetTypes**

In `src/EntityFrameworkCore.ExtensibleMigrations/HandlerDiscovery.cs` replace the `DiscoverHandlers<THandler>` method body's loop and add a new internal method. Replace the foreach body:

```csharp
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var types = SafeGetTypes(assembly)
                .Where(t =>
                    t is { IsAbstract: false, IsInterface: false } &&
                    handlerInterface.IsAssignableFrom(t) &&
                    t.GetCustomAttribute<CustomMigrationHandlerAttribute>() != null);

            handlerTypes.AddRange(types);
        }
```

Note: `CustomMigrationHandlerAttribute` is the new name (Task 6). Until Task 6 lands, keep the existing `CustomMigrationOperationHandler` reference here — adjust during Task 6.

Add at the end of the class:

```csharp
    internal static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        if (assembly.IsDynamic)
        {
            yield break;
        }

        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types;
        }
        catch
        {
            yield break;
        }

        foreach (var t in types)
        {
            if (t is not null)
            {
                yield return t;
            }
        }
    }
```

- [ ] **Step 4: Run test, verify pass**

Run: `dotnet test --filter HandlerDiscoveryReflectionTests`
Expected: 3/3 pass.

- [ ] **Step 5: Run full suite**

Run: `dotnet test`
Expected: all pass — existing 4 + new 3.

- [ ] **Step 6: Commit**

```bash
git add src/EntityFrameworkCore.ExtensibleMigrations/HandlerDiscovery.cs tests/EntityFrameworkCore.ExtensibleMigrations.Tests/HandlerDiscoveryReflectionTests.cs
git commit -m "fix(discovery): survive ReflectionTypeLoadException and dynamic assemblies

Bare assembly.GetTypes() throws when an assembly has missing transitive
references — the whole framework would fail to start. Also handle dynamic
assemblies which can throw on type enumeration depending on builder kind."
```

---

### Task 4: Replace Down() detection with explicit OperationPhase

**Critical issue C3.** Current sniff `operations[0].GetType().Name.StartsWith("Drop")` is wrong for AlterColumn-only migrations, RenameTable, empty op lists, and any migration whose first reversed op isn't a `Drop*`.

**Files:**
- Modify: `src/EntityFrameworkCore.ExtensibleMigrations/ICSharpMigrationOperationHandler.cs`
- Create: `src/EntityFrameworkCore.ExtensibleMigrations/OperationPhase.cs`
- Modify: `src/EntityFrameworkCore.ExtensibleMigrations/ExtensibleCSharpMigrationOperationGenerator.cs`
- Create: `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/ExtensibleCSharpMigrationOperationGeneratorTests.cs`

- [ ] **Step 1: Write failing test — phase ordering**

Create `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/ExtensibleCSharpMigrationOperationGeneratorTests.cs`:

```csharp
using EntityFrameworkCore.ExtensibleMigrations;
using EntityFrameworkCore.ExtensibleMigrations.Tests.Stubs;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.ExtensibleMigrations.Tests;

public class ExtensibleCSharpMigrationOperationGeneratorTests
{
    [Fact]
    public void Emits_BeforeCore_then_core_then_AfterCore_in_input_order()
    {
        var spy = new SpyCSharpMigrationOperationGenerator();
        var sp = BuildProvider();
        var sut = new ExtensibleCSharpMigrationOperationGenerator(spy, sp);
        var builder = new IndentedStringBuilder();

        var ops = new MigrationOperation[]
        {
            new SpyOperation { Marker = "A_after" },
            new AddColumnOperation { Name = "Col1", Table = "T" },
            new DropSpyOperation { Marker = "B_before" },
        };

        sut.Generate("mb", ops, builder);

        var output = builder.ToString();
        var beforeIdx = output.IndexOf("BEFORE:B_before", StringComparison.Ordinal);
        var coreIdx = output.IndexOf("CORE:AddColumnOperation", StringComparison.Ordinal);
        var afterIdx = output.IndexOf("AFTER:A_after", StringComparison.Ordinal);

        Assert.True(beforeIdx >= 0 && coreIdx >= 0 && afterIdx >= 0, output);
        Assert.True(beforeIdx < coreIdx, "BeforeCore must come before core");
        Assert.True(coreIdx < afterIdx, "AfterCore must come after core");
    }

    [Fact]
    public void No_handlers_routes_all_to_default_generator()
    {
        var spy = new SpyCSharpMigrationOperationGenerator();
        var sp = new ServiceCollection().BuildServiceProvider();
        var sut = new ExtensibleCSharpMigrationOperationGenerator(spy, sp);
        var builder = new IndentedStringBuilder();

        var ops = new MigrationOperation[] { new AddColumnOperation { Name = "X", Table = "T" } };
        sut.Generate("mb", ops, builder);

        Assert.Single(spy.Calls);
        Assert.Single(spy.Calls[0]);
    }

    private static IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddTransient<TestPhasedHandler>();
        return services.BuildServiceProvider();
    }

    [CustomMigrationHandlerAttribute(Order = 100)]
    public sealed class TestPhasedHandler : ICSharpMigrationOperationHandler
    {
        public bool CanHandle(MigrationOperation operation) => operation is SpyOperation or DropSpyOperation;

        public OperationPhase Phase(MigrationOperation operation) =>
            operation is DropSpyOperation ? OperationPhase.BeforeCore : OperationPhase.AfterCore;

        public void Generate(MigrationOperation operation, IndentedStringBuilder builder)
        {
            switch (operation)
            {
                case SpyOperation s: builder.AppendLine($"// AFTER:{s.Marker}"); break;
                case DropSpyOperation d: builder.AppendLine($"// BEFORE:{d.Marker}"); break;
            }
        }
    }
}
```

- [ ] **Step 2: Run test, verify it fails to compile**

Run: `dotnet test --filter ExtensibleCSharpMigrationOperationGeneratorTests`
Expected: FAIL — `OperationPhase` and `CustomMigrationHandlerAttribute` do not exist; `Phase` not on interface; `Generate` parameter is `object`.

- [ ] **Step 3: Add OperationPhase enum**

Create `src/EntityFrameworkCore.ExtensibleMigrations/OperationPhase.cs`:

```csharp
namespace EntityFrameworkCore.ExtensibleMigrations;

/// <summary>
/// Where a custom migration operation should appear relative to core EF Core operations
/// in the generated C# migration body.
/// </summary>
public enum OperationPhase
{
    /// <summary>
    /// Emit before EF's core operations. Use for drops, prerequisite extension installs, etc.
    /// </summary>
    BeforeCore,

    /// <summary>
    /// Emit after EF's core operations. Use for indexes, views, grants — anything that
    /// depends on tables/columns existing.
    /// </summary>
    AfterCore,
}
```

- [ ] **Step 4: Add Phase to ICSharpMigrationOperationHandler and switch builder type**

Modify `src/EntityFrameworkCore.ExtensibleMigrations/ICSharpMigrationOperationHandler.cs` to:

```csharp
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace EntityFrameworkCore.ExtensibleMigrations;

/// <summary>
/// Interface for handlers that generate C# code for migration operations during scaffolding.
/// </summary>
public interface ICSharpMigrationOperationHandler
{
    /// <summary>
    /// Determines if this handler can generate C# code for the given operation.
    /// </summary>
    bool CanHandle(MigrationOperation operation);

    /// <summary>
    /// Where this operation should appear relative to core EF operations.
    /// Default: <see cref="OperationPhase.AfterCore"/>.
    /// </summary>
    OperationPhase Phase(MigrationOperation operation) => OperationPhase.AfterCore;

    /// <summary>
    /// Generates C# code for the given operation.
    /// </summary>
    void Generate(MigrationOperation operation, IndentedStringBuilder builder);
}
```

- [ ] **Step 5: Replace generator with phase-based ordering**

Replace contents of `src/EntityFrameworkCore.ExtensibleMigrations/ExtensibleCSharpMigrationOperationGenerator.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace EntityFrameworkCore.ExtensibleMigrations;

/// <summary>
/// Wraps the default <see cref="ICSharpMigrationOperationGenerator"/> to interleave custom
/// handler-emitted operations around core EF operations, ordered by handler-declared phase.
/// </summary>
public class ExtensibleCSharpMigrationOperationGenerator(
    ICSharpMigrationOperationGenerator defaultGenerator,
    IServiceProvider serviceProvider)
    : ICSharpMigrationOperationGenerator
{
    private readonly IReadOnlyList<ICSharpMigrationOperationHandler> _handlers
        = HandlerDiscovery.CreateCSharpHandlers(serviceProvider);

    public void Generate(string builderName, IReadOnlyList<MigrationOperation> operations, IndentedStringBuilder builder)
    {
        var beforeCore = new List<(MigrationOperation Op, ICSharpMigrationOperationHandler Handler)>();
        var afterCore = new List<(MigrationOperation Op, ICSharpMigrationOperationHandler Handler)>();
        var coreOps = new List<MigrationOperation>();

        foreach (var op in operations)
        {
            var handler = _handlers.FirstOrDefault(h => h.CanHandle(op));
            if (handler is null)
            {
                coreOps.Add(op);
                continue;
            }

            switch (handler.Phase(op))
            {
                case OperationPhase.BeforeCore: beforeCore.Add((op, handler)); break;
                case OperationPhase.AfterCore: afterCore.Add((op, handler)); break;
            }
        }

        foreach (var (op, h) in beforeCore) h.Generate(op, builder);
        defaultGenerator.Generate(builderName, coreOps, builder);
        foreach (var (op, h) in afterCore) h.Generate(op, builder);
    }
}
```

- [ ] **Step 6: Run test, verify pass**

Run: `dotnet test --filter ExtensibleCSharpMigrationOperationGeneratorTests`
Expected: pass.

Note: this step depends on `CustomMigrationHandlerAttribute` existing. Since the rename happens in Task 6, write the test using the **current** name `CustomMigrationOperationHandler` and update during Task 6's grep-rename.

- [ ] **Step 7: Run full suite — paged-query consumer is broken now**

Run: `dotnet test`
Expected: extensible-migrations tests pass. Consumer paged-query in separate repo will need updates but those are out of scope here.

- [ ] **Step 8: Commit**

```bash
git add src/EntityFrameworkCore.ExtensibleMigrations/ICSharpMigrationOperationHandler.cs src/EntityFrameworkCore.ExtensibleMigrations/OperationPhase.cs src/EntityFrameworkCore.ExtensibleMigrations/ExtensibleCSharpMigrationOperationGenerator.cs tests/EntityFrameworkCore.ExtensibleMigrations.Tests/ExtensibleCSharpMigrationOperationGeneratorTests.cs
git commit -m "feat(api)!: replace Down() sniffing with explicit OperationPhase

ICSharpMigrationOperationHandler.Generate now takes IndentedStringBuilder
directly (it's public — comment claiming otherwise was wrong) and gains a
Phase(op) hook returning BeforeCore/AfterCore. The generator emits
beforeCore -> core -> afterCore in input order, killing the brittle
'first op type starts with Drop' direction heuristic that mis-classified
AlterColumn / RenameTable / mixed migrations.

BREAKING: ICSharpMigrationOperationHandler signature changed."
```

---

### Task 5: Cache CSharp generator handlers symmetric to differ

**Important issue I6.** Generator instantiates handlers per `Generate` call (called twice/migration plus snapshot work).

Already done in Task 4 — handlers cached in `_handlers` field. Verify and skip.

- [ ] **Step 1: Inspect current implementation**

Read `src/EntityFrameworkCore.ExtensibleMigrations/ExtensibleCSharpMigrationOperationGenerator.cs` and confirm `_handlers` field exists and is initialised in primary constructor expression.

- [ ] **Step 2: No-op commit message** — already covered by Task 4. Skip.

---

### Task 6: Rename `CustomMigrationOperationHandler` → `CustomMigrationHandlerAttribute`, seal

**Important issue I1.** Convention requires `Attribute` suffix; current name conflates with handler concept; `Interface` target is dead.

**Files:**
- Modify: `src/EntityFrameworkCore.ExtensibleMigrations/ExtensibleMigrationAttributes.cs`
- Modify: `src/EntityFrameworkCore.ExtensibleMigrations/HandlerDiscovery.cs`
- Modify: `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/HandlerDiscoveryTests.cs`
- Modify: `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/ExtensibleCSharpMigrationOperationGeneratorTests.cs`

- [ ] **Step 1: Rewrite attributes file**

Replace `src/EntityFrameworkCore.ExtensibleMigrations/ExtensibleMigrationAttributes.cs`:

```csharp
namespace EntityFrameworkCore.ExtensibleMigrations;

/// <summary>
/// Marks a class implementing <see cref="IMigrationOperationHandler"/> or
/// <see cref="ICSharpMigrationOperationHandler"/> for attribute-based discovery.
/// Alternatively register handlers explicitly via the DI extensions.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CustomMigrationHandlerAttribute : Attribute
{
    /// <summary>
    /// Order of execution. Lower runs first. Default 1000.
    /// </summary>
    public int Order { get; init; } = 1000;
}
```

(`CustomMigrationOperationAttribute` removed — see Task 7 for its fate.)

- [ ] **Step 2: Update HandlerDiscovery references**

In `src/EntityFrameworkCore.ExtensibleMigrations/HandlerDiscovery.cs` replace `CustomMigrationOperationHandler` with `CustomMigrationHandlerAttribute`. Two call sites: line ~43 and line ~51.

- [ ] **Step 3: Update tests**

In `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/HandlerDiscoveryTests.cs`, replace all `[CustomMigrationOperationHandler(Order = N)]` with `[CustomMigrationHandler(Order = N)]`.

In `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/ExtensibleCSharpMigrationOperationGeneratorTests.cs`, same replacement.

- [ ] **Step 4: Build + test**

Run: `dotnet test`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(api)!: rename CustomMigrationOperationHandler -> CustomMigrationHandlerAttribute

- Adds Attribute suffix per .NET convention
- Removes confusable 'handler' name on the marker attribute
- Sealed and Class-only target (Interface target was dead — discovery filtered)
- Order is now init-only

BREAKING: rename in consumer code."
```

---

### Task 7: Resolve `CustomMigrationOperationAttribute` fate — remove, update README

**Important issue I2.** Attribute is declared but never read; README claims it's required.

Decision: **remove it**. Operations are identified via `ICSharpMigrationOperationHandler.CanHandle(op)`, no marker needed. Simpler API.

**Files:**
- Already removed in Task 6's rewrite.
- Modify: `README.md`

- [ ] **Step 1: Update README quickstart**

In `README.md` remove the line `[CustomMigrationOperation]` (line 23 area) above `CreateMaterializedViewOperation`, and remove the sentence "Tag operations with `[CustomMigrationOperation]`." from line 18.

The opening of the quickstart should now read:

```csharp
public sealed class CreateMaterializedViewOperation : MigrationOperation
{
    public string ViewName { get; init; } = "";
    public string Query { get; init; } = "";
}
```

And line 18 becomes: `Tag handlers with [CustomMigrationHandler(Order = N)]. The framework finds them at design time and wires them in.`

- [ ] **Step 2: Build + test**

Run: `dotnet test`
Expected: all pass.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "refactor(api)!: drop unused [CustomMigrationOperation] attribute

Was declared but never consumed. CanHandle(op) on the C# handler is the
discrimination point; a marker attribute on the operation itself was pure
ceremony. README updated."
```

---

### Task 8: Match original service lifetime in design-time wrapper registrations

**Critical issue C1.** Wrappers registered as Singleton; EF Core registers `IMigrationsModelDiffer` as Scoped → captive dependency.

**Files:**
- Modify: `src/EntityFrameworkCore.ExtensibleMigrations/ExtensibleMigrationsDesignTimeServices.cs`
- Create: `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/ExtensibleMigrationsDesignTimeServicesTests.cs`

- [ ] **Step 1: Write failing test — preserves original lifetime**

Create `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/ExtensibleMigrationsDesignTimeServicesTests.cs`:

```csharp
using EntityFrameworkCore.ExtensibleMigrations;
using EntityFrameworkCore.ExtensibleMigrations.Tests.Stubs;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.ExtensibleMigrations.Tests;

public class ExtensibleMigrationsDesignTimeServicesTests
{
    [Fact]
    public void Preserves_scoped_lifetime_of_original_differ()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMigrationsModelDiffer, SpyMigrationsModelDiffer>();

        new ExtensibleMigrationsDesignTimeServices().ConfigureDesignTimeServices(services);

        var differDescriptor = services.Single(s => s.ServiceType == typeof(IMigrationsModelDiffer));
        Assert.Equal(ServiceLifetime.Scoped, differDescriptor.Lifetime);
    }

    [Fact]
    public void Preserves_singleton_lifetime_of_original_csharp_generator()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMigrationsModelDiffer, SpyMigrationsModelDiffer>();
        services.AddSingleton<ICSharpMigrationOperationGenerator, SpyCSharpMigrationOperationGenerator>();

        new ExtensibleMigrationsDesignTimeServices().ConfigureDesignTimeServices(services);

        var d = services.Single(s => s.ServiceType == typeof(ICSharpMigrationOperationGenerator));
        Assert.Equal(ServiceLifetime.Singleton, d.Lifetime);
    }

    [Fact]
    public void Throws_when_IMigrationsModelDiffer_not_registered()
    {
        var services = new ServiceCollection();
        var sut = new ExtensibleMigrationsDesignTimeServices();
        Assert.Throws<InvalidOperationException>(() => sut.ConfigureDesignTimeServices(services));
    }

    [Fact]
    public void Skips_csharp_generator_replacement_when_not_registered()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMigrationsModelDiffer, SpyMigrationsModelDiffer>();

        new ExtensibleMigrationsDesignTimeServices().ConfigureDesignTimeServices(services);

        Assert.DoesNotContain(services, s => s.ServiceType == typeof(ICSharpMigrationOperationGenerator));
    }
}
```

- [ ] **Step 2: Run, verify failure**

Run: `dotnet test --filter ExtensibleMigrationsDesignTimeServicesTests`
Expected: lifetime tests fail (Singleton instead of Scoped/original).

- [ ] **Step 3: Refactor design-time services to preserve lifetime**

Replace `src/EntityFrameworkCore.ExtensibleMigrations/ExtensibleMigrationsDesignTimeServices.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.ExtensibleMigrations;

/// <summary>
/// EF Core design-time services entry point. Wraps the default
/// <see cref="IMigrationsModelDiffer"/> and <see cref="ICSharpMigrationOperationGenerator"/>
/// so attribute-discovered handlers can contribute custom operations.
/// </summary>
public sealed class ExtensibleMigrationsDesignTimeServices : IDesignTimeServices
{
    public void ConfigureDesignTimeServices(IServiceCollection services)
    {
        RegisterHandlerTypes(services);
        WrapService<IMigrationsModelDiffer>(services, required: true,
            (inner, sp) => new ExtensibleMigrationsModelDiffer(inner, sp));
        WrapService<ICSharpMigrationOperationGenerator>(services, required: false,
            (inner, sp) => new ExtensibleCSharpMigrationOperationGenerator(inner, sp));
    }

    private static void RegisterHandlerTypes(IServiceCollection services)
    {
        var handlerTypes = HandlerDiscovery.OperationHandlers
            .Concat(HandlerDiscovery.CSharpHandlers)
            .Distinct();

        foreach (var t in handlerTypes)
        {
            services.AddTransient(t);
        }
    }

    private static void WrapService<TService>(
        IServiceCollection services,
        bool required,
        Func<TService, IServiceProvider, TService> wrap) where TService : class
    {
        var descriptor = services.FirstOrDefault(s => s.ServiceType == typeof(TService));
        if (descriptor is null)
        {
            if (required)
            {
                throw new InvalidOperationException(
                    $"Cannot find existing registration for {typeof(TService).Name}. " +
                    "Expected EF Core to have registered it before design-time services run.");
            }
            return;
        }

        services.Remove(descriptor);
        services.Add(new ServiceDescriptor(
            typeof(TService),
            sp => wrap(BuildOriginal(sp, descriptor), sp),
            descriptor.Lifetime));
    }

    private static TService BuildOriginal<TService>(IServiceProvider sp, ServiceDescriptor d)
        where TService : class
    {
        if (d.ImplementationFactory is not null)
            return (TService)d.ImplementationFactory(sp);
        if (d.ImplementationInstance is not null)
            return (TService)d.ImplementationInstance;
        if (d.ImplementationType is not null)
            return (TService)ActivatorUtilities.CreateInstance(sp, d.ImplementationType);

        throw new InvalidOperationException(
            $"ServiceDescriptor for {typeof(TService).Name} has no factory, instance, or type.");
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test --filter ExtensibleMigrationsDesignTimeServicesTests`
Expected: 4/4 pass.

- [ ] **Step 5: Run full suite**

Run: `dotnet test`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "fix(di): preserve original service lifetime when wrapping

Wrappers were forcibly Singleton, capturing EF's Scoped IMigrationsModelDiffer
in a singleton (captive dependency). Now copies descriptor.Lifetime. Also
factors the duplicated descriptor fan-out (factory/instance/type) into a
single helper."
```

---

## Phase 3 — Snapshot model contribution

### Task 9: Add `IMigrationsSnapshotHandler` and wrap `ICSharpSnapshotGenerator`

**Snapshot gap from review.** `ICSharpSnapshotGenerator` is public; symmetric wrap lets handlers append annotations to the snapshot so the next migration's `source` model contains them.

**Files:**
- Create: `src/EntityFrameworkCore.ExtensibleMigrations/IMigrationsSnapshotHandler.cs`
- Create: `src/EntityFrameworkCore.ExtensibleMigrations/ExtensibleCSharpSnapshotGenerator.cs`
- Modify: `src/EntityFrameworkCore.ExtensibleMigrations/HandlerDiscovery.cs`
- Modify: `src/EntityFrameworkCore.ExtensibleMigrations/ExtensibleMigrationsDesignTimeServices.cs`
- Create: `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/ExtensibleCSharpSnapshotGeneratorTests.cs`

- [ ] **Step 1: Write failing snapshot wrapper test**

Create `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/ExtensibleCSharpSnapshotGeneratorTests.cs`:

```csharp
using EntityFrameworkCore.ExtensibleMigrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.ExtensibleMigrations.Tests;

public class ExtensibleCSharpSnapshotGeneratorTests
{
    [Fact]
    public void Calls_inner_generator_then_appends_handler_output()
    {
        var inner = new SpySnapshotGenerator();
        var sp = new ServiceCollection().AddTransient<TestSnapshotHandler>().BuildServiceProvider();
        var sut = new ExtensibleCSharpSnapshotGenerator(inner, sp);
        var builder = new IndentedStringBuilder();

        var modelBuilder = new ModelBuilder();
        sut.Generate("mb", modelBuilder.Model, builder);

        var output = builder.ToString();
        var innerIdx = output.IndexOf("INNER:", StringComparison.Ordinal);
        var handlerIdx = output.IndexOf("HANDLER:Searchable:CreatedIndex", StringComparison.Ordinal);

        Assert.True(innerIdx >= 0);
        Assert.True(handlerIdx > innerIdx, "Handler must run after inner generator");
    }

    private sealed class SpySnapshotGenerator : ICSharpSnapshotGenerator
    {
        public void Generate(string builderName, IModel model, IndentedStringBuilder builder)
            => builder.AppendLine("// INNER:" + builderName);
    }

    [CustomMigrationHandler(Order = 100)]
    public sealed class TestSnapshotHandler : IMigrationsSnapshotHandler
    {
        public void GenerateSnapshot(IModel model, IndentedStringBuilder builder)
            => builder.AppendLine("// HANDLER:Searchable:CreatedIndex:ix_foo");
    }
}
```

- [ ] **Step 2: Run, verify failure (types missing)**

Run: `dotnet test --filter ExtensibleCSharpSnapshotGeneratorTests`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Add IMigrationsSnapshotHandler**

Create `src/EntityFrameworkCore.ExtensibleMigrations/IMigrationsSnapshotHandler.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityFrameworkCore.ExtensibleMigrations;

/// <summary>
/// Handler that appends additional snapshot code after the default snapshot generator.
/// Use to write modelBuilder.HasAnnotation(...) calls or similar so handler-managed state
/// becomes part of the source model on the next migration diff.
/// </summary>
public interface IMigrationsSnapshotHandler
{
    /// <summary>
    /// Appends snapshot code. Must be deterministic — same model in, same output out.
    /// </summary>
    void GenerateSnapshot(IModel model, IndentedStringBuilder builder);
}
```

- [ ] **Step 4: Add wrapper**

Create `src/EntityFrameworkCore.ExtensibleMigrations/ExtensibleCSharpSnapshotGenerator.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Design;

namespace EntityFrameworkCore.ExtensibleMigrations;

/// <summary>
/// Wraps the default <see cref="ICSharpSnapshotGenerator"/> so registered
/// <see cref="IMigrationsSnapshotHandler"/>s can append their own state to the snapshot.
/// </summary>
public sealed class ExtensibleCSharpSnapshotGenerator(
    ICSharpSnapshotGenerator inner,
    IServiceProvider serviceProvider)
    : ICSharpSnapshotGenerator
{
    private readonly IReadOnlyList<IMigrationsSnapshotHandler> _handlers
        = HandlerDiscovery.CreateSnapshotHandlers(serviceProvider);

    public void Generate(string builderName, IModel model, IndentedStringBuilder builder)
    {
        inner.Generate(builderName, model, builder);
        foreach (var h in _handlers)
        {
            h.GenerateSnapshot(model, builder);
        }
    }
}
```

- [ ] **Step 5: Add discovery for snapshot handlers**

In `src/EntityFrameworkCore.ExtensibleMigrations/HandlerDiscovery.cs` add:

```csharp
    /// <summary>Discovered snapshot handler types.</summary>
    public static IReadOnlyList<Type> SnapshotHandlers { get; } = DiscoverHandlers<IMigrationsSnapshotHandler>();

    /// <summary>Creates instances of all discovered snapshot handlers.</summary>
    public static IReadOnlyList<IMigrationsSnapshotHandler> CreateSnapshotHandlers(IServiceProvider serviceProvider)
        => SnapshotHandlers
            .Select(t => (IMigrationsSnapshotHandler)ActivatorUtilities.CreateInstance(serviceProvider, t))
            .ToList();
```

Update the `RegisterHandlerTypes` in `ExtensibleMigrationsDesignTimeServices`:

```csharp
        var handlerTypes = HandlerDiscovery.OperationHandlers
            .Concat(HandlerDiscovery.CSharpHandlers)
            .Concat(HandlerDiscovery.SnapshotHandlers)
            .Distinct();
```

- [ ] **Step 6: Wire snapshot generator wrap**

In `src/EntityFrameworkCore.ExtensibleMigrations/ExtensibleMigrationsDesignTimeServices.cs` `ConfigureDesignTimeServices` body, after the existing two `WrapService` calls add:

```csharp
        WrapService<ICSharpSnapshotGenerator>(services, required: false,
            (inner, sp) => new ExtensibleCSharpSnapshotGenerator(inner, sp));
```

- [ ] **Step 7: Run snapshot test**

Run: `dotnet test --filter ExtensibleCSharpSnapshotGeneratorTests`
Expected: pass.

- [ ] **Step 8: Run full suite**

Run: `dotnet test`
Expected: all pass.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: snapshot handlers can contribute to ModelSnapshot.cs

New IMigrationsSnapshotHandler interface + ExtensibleCSharpSnapshotGenerator
wrapper that runs after EF's default snapshot generator. Handlers write
modelBuilder.HasAnnotation(...) calls so the next migration's source model
contains state owned by the handler — closes the snapshot-gap that forced
consumers to re-derive state from the live model on every diff."
```

---

## Phase 4 — Test coverage expansion

### Task 10: Differ wrapper unit tests

**Files:**
- Create: `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/ExtensibleMigrationsModelDifferTests.cs`

- [ ] **Step 1: Write tests**

Create `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/ExtensibleMigrationsModelDifferTests.cs`:

```csharp
using EntityFrameworkCore.ExtensibleMigrations;
using EntityFrameworkCore.ExtensibleMigrations.Tests.Stubs;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.ExtensibleMigrations.Tests;

public class ExtensibleMigrationsModelDifferTests
{
    [Fact]
    public void HasDifferences_true_when_only_handler_reports_diff()
    {
        var inner = new SpyMigrationsModelDiffer { ReturnHasDifferences = false };
        var sp = new ServiceCollection().AddTransient<HandlerSaysYes>().BuildServiceProvider();
        var sut = new ExtensibleMigrationsModelDiffer(inner, sp);

        Assert.True(sut.HasDifferences(null, null));
    }

    [Fact]
    public void GetDifferences_concatenates_default_then_handler_ops()
    {
        var coreOp = new AddColumnOperation { Name = "X", Table = "T" };
        var handlerOp = new SpyOperation { Marker = "H" };
        var inner = new SpyMigrationsModelDiffer { ReturnDifferences = new[] { coreOp } };
        var sp = new ServiceCollection().AddTransient<HandlerEmitsOp>().BuildServiceProvider();
        var sut = new ExtensibleMigrationsModelDiffer(inner, sp);

        var diffs = sut.GetDifferences(null, null);

        Assert.Equal(2, diffs.Count);
        Assert.Same(coreOp, diffs[0]);
        Assert.IsType<SpyOperation>(diffs[1]);
    }

    [Fact]
    public void GetDifferences_passes_existing_ops_to_subsequent_handlers()
    {
        // Order = 100 emits one op; Order = 200 inspects existingOperations and confirms it sees the first handler's op.
        var inner = new SpyMigrationsModelDiffer();
        var sp = new ServiceCollection()
            .AddTransient<HandlerOrder100Emits>()
            .AddTransient<HandlerOrder200Inspects>()
            .BuildServiceProvider();
        var sut = new ExtensibleMigrationsModelDiffer(inner, sp);

        sut.GetDifferences(null, null);

        Assert.True(HandlerOrder200Inspects.SawHandlerOrder100Op);
    }

    [CustomMigrationHandler(Order = 100)]
    public sealed class HandlerSaysYes : IMigrationOperationHandler
    {
        public bool HasDifferences(IRelationalModel? s, IRelationalModel? t, bool d) => true;
        public IReadOnlyList<MigrationOperation> GetOperations(IRelationalModel? s, IRelationalModel? t, IReadOnlyList<MigrationOperation> e) => Array.Empty<MigrationOperation>();
    }

    [CustomMigrationHandler(Order = 100)]
    public sealed class HandlerEmitsOp : IMigrationOperationHandler
    {
        public bool HasDifferences(IRelationalModel? s, IRelationalModel? t, bool d) => false;
        public IReadOnlyList<MigrationOperation> GetOperations(IRelationalModel? s, IRelationalModel? t, IReadOnlyList<MigrationOperation> e)
            => new[] { new SpyOperation { Marker = "H" } };
    }

    [CustomMigrationHandler(Order = 100)]
    public sealed class HandlerOrder100Emits : IMigrationOperationHandler
    {
        public bool HasDifferences(IRelationalModel? s, IRelationalModel? t, bool d) => false;
        public IReadOnlyList<MigrationOperation> GetOperations(IRelationalModel? s, IRelationalModel? t, IReadOnlyList<MigrationOperation> e)
            => new[] { new SpyOperation { Marker = "from-100" } };
    }

    [CustomMigrationHandler(Order = 200)]
    public sealed class HandlerOrder200Inspects : IMigrationOperationHandler
    {
        public static bool SawHandlerOrder100Op;
        public bool HasDifferences(IRelationalModel? s, IRelationalModel? t, bool d) => false;
        public IReadOnlyList<MigrationOperation> GetOperations(IRelationalModel? s, IRelationalModel? t, IReadOnlyList<MigrationOperation> e)
        {
            SawHandlerOrder100Op = e.OfType<SpyOperation>().Any(o => o.Marker == "from-100");
            return Array.Empty<MigrationOperation>();
        }
    }
}
```

- [ ] **Step 2: Run**

Run: `dotnet test --filter ExtensibleMigrationsModelDifferTests`
Expected: 3/3 pass.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "test: cover differ wrapper concat + handler-sees-prior-ops semantics"
```

---

### Task 11: End-to-end migration scaffolding test

Highest-leverage missing test. Uses Sqlite + `IMigrationsScaffolder` to drive the full pipeline.

**Files:**
- Create: `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/EndToEndMigrationScaffoldingTests.cs`
- Create: `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/Stubs/E2EHandlers.cs`

- [ ] **Step 1: Write E2E handler stubs**

Create `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/Stubs/E2EHandlers.cs`:

```csharp
using EntityFrameworkCore.ExtensibleMigrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace EntityFrameworkCore.ExtensibleMigrations.Tests.Stubs;

public sealed class E2EMarkerOperation : MigrationOperation
{
    public string Marker { get; init; } = "";
}

[CustomMigrationHandler(Order = 100)]
public sealed class E2EOperationHandler : IMigrationOperationHandler
{
    public bool HasDifferences(IRelationalModel? s, IRelationalModel? t, bool defaultHasDifferences) => defaultHasDifferences;

    public IReadOnlyList<MigrationOperation> GetOperations(IRelationalModel? s, IRelationalModel? t, IReadOnlyList<MigrationOperation> existing)
    {
        if (t is null) return Array.Empty<MigrationOperation>();
        return new MigrationOperation[] { new E2EMarkerOperation { Marker = "E2E_OP_EMITTED" } };
    }
}

[CustomMigrationHandler(Order = 100)]
public sealed class E2ECSharpHandler : ICSharpMigrationOperationHandler
{
    public bool CanHandle(MigrationOperation op) => op is E2EMarkerOperation;
    public OperationPhase Phase(MigrationOperation op) => OperationPhase.AfterCore;
    public void Generate(MigrationOperation op, IndentedStringBuilder builder)
    {
        var m = (E2EMarkerOperation)op;
        builder.AppendLine($"migrationBuilder.Sql(\"-- {m.Marker}\");");
    }
}

[CustomMigrationHandler(Order = 100)]
public sealed class E2ESnapshotHandler : IMigrationsSnapshotHandler
{
    public void GenerateSnapshot(IModel model, IndentedStringBuilder builder)
        => builder.AppendLine("// E2E_SNAPSHOT_HANDLER_RAN");
}
```

- [ ] **Step 2: Write E2E test**

Create `tests/EntityFrameworkCore.ExtensibleMigrations.Tests/EndToEndMigrationScaffoldingTests.cs`:

```csharp
using EntityFrameworkCore.ExtensibleMigrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.ExtensibleMigrations.Tests;

public class EndToEndMigrationScaffoldingTests
{
    private sealed class Widget { public int Id { get; set; } public string Name { get; set; } = ""; }

    private sealed class TestContext : DbContext
    {
        public DbSet<Widget> Widgets => Set<Widget>();
        protected override void OnConfiguring(DbContextOptionsBuilder o) => o.UseSqlite("DataSource=:memory:");
    }

    [Fact]
    public void Scaffolded_migration_includes_custom_op_emit_in_AfterCore_phase()
    {
        using var ctx = new TestContext();

        var designServices = new ServiceCollection();
        designServices.AddEntityFrameworkDesignTimeServices();
        new SqliteDesignTimeServices().ConfigureDesignTimeServices(designServices);
        new ExtensibleMigrationsDesignTimeServices().ConfigureDesignTimeServices(designServices);
        designServices.AddDbContextDesignTimeServices(ctx);

        var sp = designServices.BuildServiceProvider();
        var scaffolder = sp.GetRequiredService<IMigrationsScaffolder>();

        var migration = scaffolder.ScaffoldMigration("Init", "TestNs");

        Assert.Contains("E2E_OP_EMITTED", migration.MigrationCode);
        Assert.Contains("E2E_SNAPSHOT_HANDLER_RAN", migration.SnapshotCode);
        var sqlIdx = migration.MigrationCode.IndexOf("E2E_OP_EMITTED", StringComparison.Ordinal);
        var createTableIdx = migration.MigrationCode.IndexOf("CreateTable", StringComparison.Ordinal);
        Assert.True(createTableIdx > 0 && sqlIdx > createTableIdx, "AfterCore op must follow CreateTable in Up()");
    }
}
```

Note: `AddDbContextDesignTimeServices` is the standard EF Core extension for design-time scaffolding. If the API name in EF 10 differs slightly (e.g. `DbContextActivator` for instantiation), look at EF Core's `dotnet ef`'s own setup in `Microsoft.EntityFrameworkCore.Design.Internal` — that's the canonical source.

- [ ] **Step 3: Run**

Run: `dotnet test --filter EndToEndMigrationScaffoldingTests`
Expected: pass. If API mismatch on the design service registration, fix per EF Core 10 internals.

- [ ] **Step 4: Add second test for snapshot becoming source on next diff**

Append to the test class:

```csharp
    [Fact]
    public void Second_scaffold_after_no_changes_emits_no_handler_ops()
    {
        // Scaffold twice. The second time, the snapshot from the first should
        // make the differ see no changes, so the handler emits no operations.
        // This pins the snapshot-gap fix end-to-end.
        // See E2EOperationHandler — it currently always emits when target != null,
        // so this test will fail until the handler is taught to diff against
        // its own snapshot annotations. For now Skip-pending.
    }
```

Mark as `[Fact(Skip = "Requires snapshot-aware E2E handler — Phase 5 item")]` for now to keep CI green.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "test: end-to-end migration scaffolding via Sqlite design-time pipeline

Drives IMigrationsScaffolder.ScaffoldMigration through the wrapped services
and asserts the generated MigrationCode and SnapshotCode contain handler
contributions in the right phases."
```

---

## Phase 5 — Explicit DI registration story

### Task 12: Public DI extension methods + make HandlerDiscovery internal

**Important issue I3 + OSS readiness.** Move from "static cache + reflection at first access" to "users register explicitly via extensions; attribute discovery is the convenience fallback."

**Files:**
- Modify: `src/EntityFrameworkCore.ExtensibleMigrations/HandlerDiscovery.cs` (visibility)
- Create: `src/EntityFrameworkCore.ExtensibleMigrations/ServiceCollectionExtensions.cs`
- Modify: `src/EntityFrameworkCore.ExtensibleMigrations/ExtensibleMigrationsDesignTimeServices.cs`
- Modify: `src/EntityFrameworkCore.ExtensibleMigrations/ExtensibleMigrationsModelDiffer.cs`
- Modify: `src/EntityFrameworkCore.ExtensibleMigrations/ExtensibleCSharpMigrationOperationGenerator.cs`
- Modify: `src/EntityFrameworkCore.ExtensibleMigrations/ExtensibleCSharpSnapshotGenerator.cs`

- [ ] **Step 1: Add public DI extension methods**

Create `src/EntityFrameworkCore.ExtensibleMigrations/ServiceCollectionExtensions.cs`:

```csharp
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.ExtensibleMigrations;

/// <summary>
/// Extensions for registering ExtensibleMigrations handlers with the design-time service collection.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMigrationOperationHandler<T>(this IServiceCollection services)
        where T : class, IMigrationOperationHandler
        => services.AddTransient<IMigrationOperationHandler, T>();

    public static IServiceCollection AddCSharpMigrationOperationHandler<T>(this IServiceCollection services)
        where T : class, ICSharpMigrationOperationHandler
        => services.AddTransient<ICSharpMigrationOperationHandler, T>();

    public static IServiceCollection AddMigrationsSnapshotHandler<T>(this IServiceCollection services)
        where T : class, IMigrationsSnapshotHandler
        => services.AddTransient<IMigrationsSnapshotHandler, T>();

    /// <summary>
    /// Discovers handler types in the given assembly via [CustomMigrationHandler] attributes
    /// and registers them under the appropriate handler interface.
    /// </summary>
    public static IServiceCollection AddExtensibleMigrationsFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        foreach (var type in HandlerDiscovery.SafeGetTypes(assembly))
        {
            if (type is not { IsAbstract: false, IsInterface: false }) continue;
            if (type.GetCustomAttribute<CustomMigrationHandlerAttribute>() is null) continue;

            if (typeof(IMigrationOperationHandler).IsAssignableFrom(type))
                services.AddTransient(typeof(IMigrationOperationHandler), type);
            if (typeof(ICSharpMigrationOperationHandler).IsAssignableFrom(type))
                services.AddTransient(typeof(ICSharpMigrationOperationHandler), type);
            if (typeof(IMigrationsSnapshotHandler).IsAssignableFrom(type))
                services.AddTransient(typeof(IMigrationsSnapshotHandler), type);
        }
        return services;
    }
}
```

- [ ] **Step 2: Switch wrappers from HandlerDiscovery to GetServices**

`src/EntityFrameworkCore.ExtensibleMigrations/ExtensibleMigrationsModelDiffer.cs` line 16: replace
```csharp
private readonly IReadOnlyList<IMigrationOperationHandler> _handlers = HandlerDiscovery.CreateOperationHandlers(serviceProvider);
```
with:
```csharp
private readonly IReadOnlyList<IMigrationOperationHandler> _handlers
    = serviceProvider.GetServices<IMigrationOperationHandler>()
        .OrderBy(h => h.GetType().GetCustomAttribute<CustomMigrationHandlerAttribute>()?.Order ?? 1000)
        .ToList();
```

Add `using Microsoft.Extensions.DependencyInjection;` and `using System.Reflection;`.

Same for `ExtensibleCSharpMigrationOperationGenerator._handlers` and `ExtensibleCSharpSnapshotGenerator._handlers`.

- [ ] **Step 3: Make HandlerDiscovery internal — keep `SafeGetTypes` accessible**

In `src/EntityFrameworkCore.ExtensibleMigrations/HandlerDiscovery.cs` change `public static class HandlerDiscovery` → `internal static class HandlerDiscovery`. The `InternalsVisibleTo` from Task 2 keeps tests working.

`SafeGetTypes` stays internal. `ServiceCollectionExtensions` is in the same assembly so it sees it.

- [ ] **Step 4: Update design-time-services to use AddExtensibleMigrationsFromAssembly for auto-discovery**

In `src/EntityFrameworkCore.ExtensibleMigrations/ExtensibleMigrationsDesignTimeServices.cs` replace `RegisterHandlerTypes` with:

```csharp
    private static void RegisterHandlerTypes(IServiceCollection services)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            services.AddExtensibleMigrationsFromAssembly(assembly);
        }
    }
```

- [ ] **Step 5: Run tests**

Run: `dotnet test`
Expected: all pass. Test handler classes need to be public (already are) and tagged (already are).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor(api): explicit DI extensions + assembly-scan fallback

Public AddMigrationOperationHandler/AddCSharpMigrationOperationHandler/
AddMigrationsSnapshotHandler/AddExtensibleMigrationsFromAssembly extensions.
Wrappers now resolve handlers via GetServices<>() instead of static
HandlerDiscovery scan. HandlerDiscovery is now internal."
```

---

## Phase 6 — OSS hygiene

### Task 13: LICENSE, .editorconfig, CHANGELOG, CONTRIBUTING

**Files:**
- Create: `LICENSE`
- Create: `.editorconfig`
- Create: `CHANGELOG.md`
- Create: `CONTRIBUTING.md`

- [ ] **Step 1: Add MIT LICENSE**

Create `LICENSE` with standard MIT text, copyright `2026 Sherman Rose`. (Stock template — substitute year and name into the canonical MIT text.)

- [ ] **Step 2: Add .editorconfig**

Create `.editorconfig`:

```
root = true

[*]
charset = utf-8
end_of_line = lf
insert_final_newline = true
trim_trailing_whitespace = true
indent_style = space
indent_size = 4

[*.{yml,yaml,json}]
indent_size = 2

[*.md]
trim_trailing_whitespace = false
```

- [ ] **Step 3: Add CHANGELOG.md**

Create `CHANGELOG.md`:

```markdown
# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
Note: pre-1.0 versions may include breaking changes in any minor release.

## [Unreleased]

### Added
- `IMigrationsSnapshotHandler` interface and `ExtensibleCSharpSnapshotGenerator` so handlers can contribute to ModelSnapshot.cs.
- `OperationPhase` enum on `ICSharpMigrationOperationHandler` for explicit Up/Down ordering.
- Public DI extensions: `AddMigrationOperationHandler<T>`, `AddCSharpMigrationOperationHandler<T>`, `AddMigrationsSnapshotHandler<T>`, `AddExtensibleMigrationsFromAssembly`.

### Changed (BREAKING)
- `ICSharpMigrationOperationHandler.Generate` parameter type `object` → `IndentedStringBuilder`.
- `CustomMigrationOperationHandler` attribute renamed to `CustomMigrationHandlerAttribute`.

### Removed (BREAKING)
- `CustomMigrationOperationAttribute` (was unused).
- Down-method-detection via op-name prefix; replaced with explicit `OperationPhase`.

### Fixed
- Service lifetime mismatch — wrapper now copies original `IMigrationsModelDiffer` lifetime instead of forcing Singleton.
- Reflection scanning now survives `ReflectionTypeLoadException` and dynamic assemblies.
```

- [ ] **Step 4: Add CONTRIBUTING.md**

Create `CONTRIBUTING.md`:

```markdown
# Contributing

Thanks for your interest. This is a small library — keep PRs focused.

## Build

```bash
dotnet build -c Release
```

## Test

```bash
dotnet test
```

End-to-end scaffolding tests use Sqlite in-memory; no external services needed.

## Style

- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` is on. CI fails on warnings.
- Public API additions need XML doc comments.
- Match existing patterns; .editorconfig pins formatting.

## PRs

- Run `dotnet test` locally before opening.
- One topic per PR. If your change touches handler discovery + the differ wrapper, that's two PRs unless the change is genuinely indivisible.
- Update CHANGELOG.md under `[Unreleased]`.
```

- [ ] **Step 5: Commit**

```bash
git add LICENSE .editorconfig CHANGELOG.md CONTRIBUTING.md
git commit -m "docs: add LICENSE, .editorconfig, CHANGELOG, CONTRIBUTING"
```

---

### Task 14: GitHub repo configuration files

**Files:**
- Create: `.github/dependabot.yml`
- Create: `.github/ISSUE_TEMPLATE/bug_report.yml`
- Create: `.github/ISSUE_TEMPLATE/feature_request.yml`
- Create: `.github/PULL_REQUEST_TEMPLATE.md`

- [ ] **Step 1: Add dependabot**

Create `.github/dependabot.yml`:

```yaml
version: 2
updates:
  - package-ecosystem: nuget
    directory: "/"
    schedule:
      interval: weekly
    groups:
      ef-core:
        patterns:
          - "Microsoft.EntityFrameworkCore*"
  - package-ecosystem: github-actions
    directory: "/"
    schedule:
      interval: monthly
```

- [ ] **Step 2: Add bug report template**

Create `.github/ISSUE_TEMPLATE/bug_report.yml`:

```yaml
name: Bug report
description: Report something broken
labels: [bug]
body:
  - type: textarea
    id: what
    attributes:
      label: What happened
      description: What did you expect, what happened instead.
    validations:
      required: true
  - type: textarea
    id: repro
    attributes:
      label: Repro
      description: Smallest code that triggers it.
    validations:
      required: true
  - type: input
    id: efver
    attributes:
      label: EF Core version
    validations:
      required: true
  - type: input
    id: pkgver
    attributes:
      label: ExtensibleMigrations version
    validations:
      required: true
```

- [ ] **Step 3: Add feature request template**

Create `.github/ISSUE_TEMPLATE/feature_request.yml`:

```yaml
name: Feature request
description: Propose an enhancement
labels: [enhancement]
body:
  - type: textarea
    id: problem
    attributes:
      label: Problem
      description: What can't you do today?
    validations:
      required: true
  - type: textarea
    id: proposal
    attributes:
      label: Proposed API or approach
    validations:
      required: false
```

- [ ] **Step 4: Add PR template**

Create `.github/PULL_REQUEST_TEMPLATE.md`:

```markdown
## What

<!-- One sentence. -->

## Why

<!-- The motivation. -->

## Notes

- [ ] Tests added / updated
- [ ] CHANGELOG.md updated under `[Unreleased]`
- [ ] Public API changes documented
```

- [ ] **Step 5: Commit**

```bash
git add .github/
git commit -m "docs: add dependabot config and issue/PR templates"
```

---

### Task 15: CI hardening — coverage, OS matrix, permissions, pack validation

**Files:**
- Modify: `.github/workflows/ci.yml`

- [ ] **Step 1: Replace ci.yml**

Replace `.github/workflows/ci.yml`:

```yaml
name: ci

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

permissions:
  contents: read

jobs:
  build-and-test:
    strategy:
      fail-fast: false
      matrix:
        os: [ubuntu-latest, windows-latest, macos-latest]
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet restore
      - run: dotnet build -c Release --no-restore
      - run: dotnet test -c Release --no-build --logger "trx;LogFileName=test-results.trx" --collect:"XPlat Code Coverage"
      - run: dotnet pack -c Release --no-build -o ./artifacts
      - uses: actions/upload-artifact@v4
        if: always()
        with:
          name: test-results-${{ matrix.os }}
          path: |
            **/test-results.trx
            **/coverage.cobertura.xml
      - uses: actions/upload-artifact@v4
        if: matrix.os == 'ubuntu-latest'
        with:
          name: nupkg
          path: ./artifacts/*.nupkg
```

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: OS matrix, code coverage, dotnet pack validation, restrict permissions"
```

---

### Task 16: Tag-based publish workflow

**Files:**
- Create: `.github/workflows/release.yml`

- [ ] **Step 1: Create release workflow**

Create `.github/workflows/release.yml`:

```yaml
name: release

on:
  push:
    tags: ['v*']

permissions:
  contents: read

jobs:
  publish:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet restore
      - run: dotnet build -c Release --no-restore
      - run: dotnet test -c Release --no-build
      - run: dotnet pack -c Release --no-build -o ./artifacts -p:VersionPrefix=${GITHUB_REF_NAME#v}
      - run: dotnet nuget push ./artifacts/*.nupkg --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json --skip-duplicate
      - run: dotnet nuget push ./artifacts/*.snupkg --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json --skip-duplicate
```

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "ci: add tag-triggered NuGet publish workflow"
```

---

### Task 17: VersionPrefix and IsTrimmable explicit decision

**Files:**
- Modify: `Directory.Build.props`

- [ ] **Step 1: Set version + trim**

In `Directory.Build.props`'s first `<PropertyGroup>` add:

```xml
    <VersionPrefix>0.1.0</VersionPrefix>
    <VersionSuffix>preview.1</VersionSuffix>
    <IsTrimmable>false</IsTrimmable>
```

`IsTrimmable=false` is correct: design-time package, reflection-heavy, never AOT-published in normal usage.

- [ ] **Step 2: Build, verify packed version**

Run: `dotnet pack -c Release -o ./artifacts && ls ./artifacts`
Expected: `EntityFrameworkCore.ExtensibleMigrations.0.1.0-preview.1.nupkg` and matching `.snupkg`.

- [ ] **Step 3: Commit**

```bash
git add Directory.Build.props
git commit -m "build: pin VersionPrefix=0.1.0-preview.1; opt out of trim warnings

Design-time packages can't sensibly be trimmed (reflection over user
assemblies). Setting IsTrimmable=false makes the choice explicit so the
default doesn't change underneath us."
```

---

### Task 18: Sample project — materialised view end to end

The single highest-leverage thing for adoption (per review). Working consumer that someone can `git clone && dotnet run`.

**Files:**
- Create: `samples/MaterializedViewSample/MaterializedViewSample.csproj`
- Create: `samples/MaterializedViewSample/Program.cs`
- Create: `samples/MaterializedViewSample/Domain.cs`
- Create: `samples/MaterializedViewSample/MaterializedViewExtensions.cs`
- Create: `samples/MaterializedViewSample/CreateMaterializedViewOperation.cs`
- Create: `samples/MaterializedViewSample/MaterializedViewHandlers.cs`
- Create: `samples/README.md`
- Modify: `ExtensibleMigrations.slnx`

- [ ] **Step 1: Create sample csproj**

Create `samples/MaterializedViewSample/MaterializedViewSample.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.7" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.7" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\EntityFrameworkCore.ExtensibleMigrations\EntityFrameworkCore.ExtensibleMigrations.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create domain + DbContext**

Create `samples/MaterializedViewSample/Domain.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace MaterializedViewSample;

public sealed class Order
{
    public int Id { get; set; }
    public string Customer { get; set; } = "";
    public decimal Total { get; set; }
}

public sealed class OrderContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
    protected override void OnConfiguring(DbContextOptionsBuilder o) => o.UseSqlite("DataSource=orders.db");
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Order>().HasMaterializedView("OrderTotalsByCustomer",
            "SELECT Customer, SUM(Total) AS Total FROM Orders GROUP BY Customer");
    }
}
```

- [ ] **Step 3: Create operation type**

Create `samples/MaterializedViewSample/CreateMaterializedViewOperation.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace MaterializedViewSample;

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

- [ ] **Step 4: Create model-builder annotations + handlers**

Create `samples/MaterializedViewSample/MaterializedViewExtensions.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaterializedViewSample;

public static class MaterializedViewExtensions
{
    public const string ViewNameAnnotation = "MatView:Name";
    public const string ViewQueryAnnotation = "MatView:Query";

    public static EntityTypeBuilder<T> HasMaterializedView<T>(
        this EntityTypeBuilder<T> b, string viewName, string query) where T : class
    {
        b.HasAnnotation(ViewNameAnnotation, viewName);
        b.HasAnnotation(ViewQueryAnnotation, query);
        return b;
    }
}
```

Create `samples/MaterializedViewSample/MaterializedViewHandlers.cs`:

```csharp
using EntityFrameworkCore.ExtensibleMigrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace MaterializedViewSample;

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
            var n = et.FindAnnotation(MaterializedViewExtensions.ViewNameAnnotation)?.Value as string;
            var q = et.FindAnnotation(MaterializedViewExtensions.ViewQueryAnnotation)?.Value as string;
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
                builder.AppendLine($"migrationBuilder.Sql(\"CREATE VIEW {c.ViewName} AS {c.Query};\");");
                break;
            case DropMaterializedViewOperation d:
                builder.AppendLine($"migrationBuilder.Sql(\"DROP VIEW IF EXISTS {d.ViewName};\");");
                break;
        }
    }
}
```

(Note: Sqlite uses `CREATE VIEW`, not `CREATE MATERIALIZED VIEW` — the sample is illustrative. PostgreSQL would use the latter; the README quickstart will note this.)

- [ ] **Step 5: Create program entry point**

Create `samples/MaterializedViewSample/Program.cs`:

```csharp
using MaterializedViewSample;

Console.WriteLine("Run `dotnet ef migrations add Init` from this folder to scaffold a migration.");
Console.WriteLine("The generated migration's Up() will include the CREATE VIEW for OrderTotalsByCustomer.");

using var ctx = new OrderContext();
ctx.Database.EnsureCreated();
```

- [ ] **Step 6: Add samples README**

Create `samples/README.md`:

```markdown
# Samples

## MaterializedViewSample

Adds a `[HasMaterializedView]` extension to EntityTypeBuilder. The handlers
emit `CreateMaterializedViewOperation` / `DropMaterializedViewOperation` from
the diff and turn them into `migrationBuilder.Sql(...)` in the migration.

```bash
cd samples/MaterializedViewSample
dotnet ef migrations add Init
```

Inspect `Migrations/*_Init.cs` — Up() ends with the CREATE VIEW; Down() begins with DROP VIEW.
```

- [ ] **Step 7: Add to solution**

In `ExtensibleMigrations.slnx`, add a new project entry for the sample. (slnx is XML; mirror the existing entries.)

- [ ] **Step 8: Build sample**

Run: `dotnet build samples/MaterializedViewSample/MaterializedViewSample.csproj`
Expected: succeeds.

- [ ] **Step 9: Commit**

```bash
git add samples/ ExtensibleMigrations.slnx
git commit -m "samples: add MaterializedViewSample as worked example

End-to-end runnable project demonstrating handler-emitted CREATE VIEW.
Highest-leverage adoption aid per code review."
```

---

### Task 19: README polish — DesignTimeServicesReference note, link to sample, remove bad attribute reference

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Update README**

In `README.md`:

1. Quickstart already updated in Task 7. Re-verify it does not mention `[CustomMigrationOperation]`.
2. After the "Design-time service registration" section, add a paragraph:

```markdown
### When migrations live in a separate assembly

If your migrations assembly does not directly reference this package, EF Core
will not auto-discover the design-time services. Add this attribute to your
migrations or DbContext assembly:

```csharp
[assembly: DesignTimeServicesReference(
    "EntityFrameworkCore.ExtensibleMigrations.ExtensibleMigrationsDesignTimeServices, EntityFrameworkCore.ExtensibleMigrations")]
```
```

3. Replace `## Quickstart — materialised view handler` content's stub bodies with a one-line link: `See [samples/MaterializedViewSample](samples/MaterializedViewSample) for a complete worked example.`

4. Update Status section: `0.1.0-preview.1`. API unstable until 1.0.

5. Add a `## API stability` section: 

```markdown
## API stability

Pre-1.0. Minor versions may include breaking changes — see CHANGELOG.md.
Once 1.0 is tagged, semantic versioning applies normally.
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs: link to sample, document DesignTimeServicesReference, declare pre-1.0 stability"
```

---

## Self-Review

**Spec coverage** (mapping back to code review):
- C1 lifetime mismatch → Task 8.
- C2 reflection robustness → Task 3.
- C3 Down() detection → Task 4.
- C4 `object builder` → Task 4 (bundled in same API break).
- I1 attribute rename → Task 6.
- I2 dead `CustomMigrationOperation` attribute → Task 7.
- I3 redundant `AddTransient` → Task 12 (replaced with explicit DI extensions).
- I6 generator caching → Task 4.
- I7 trim/AOT → Task 17.
- Snapshot gap → Task 9.
- OSS readiness → Tasks 13–18.
- Test strategy → Tasks 10, 11 + per-feature tests inside each fix.

**Placeholder scan:** None — every step contains the actual code or command.

**Type consistency:** `CustomMigrationHandlerAttribute` used post-Task 6; tests written in Task 4 use the old name and are renamed in Task 6 — flagged in Task 4 Step 6 note. `OperationPhase` introduced Task 4 and used consistently after. `IMigrationsSnapshotHandler` stable name across Tasks 9, 11, 12, 18.

One open item flagged inline: end-to-end second-scaffold test in Task 11 Step 4 is intentionally skipped pending a snapshot-aware E2E handler — left as a follow-up issue, not a Phase 4 blocker.

---

Plan complete and saved to `docs/superpowers/plans/2026-04-25-extensible-migrations-hardening.md`. Two execution options:

1. **Subagent-Driven (recommended)** — fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** — execute tasks in this session using executing-plans, batch with checkpoints.

Which approach?
