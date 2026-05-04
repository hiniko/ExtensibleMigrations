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
        WriteDirectoryBuildProps(dest, RepoRoot());
        InstallLocalToolManifest(dest, RepoRoot());
        RestoreProject(dest);
        RestoreLocalTools(dest);
        return new FixtureProject(dest, fixtureName);
    }

    public string ReadGenerated(string relativePath) =>
        File.ReadAllText(Path.Combine(ProjectDir, relativePath));

    public string[] ListMigrationFiles()
    {
        var migrationsDir = Path.Combine(ProjectDir, "Migrations");
        if (!Directory.Exists(migrationsDir))
            return Array.Empty<string>();
        return Directory.GetFiles(migrationsDir, "*.cs");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(ProjectDir, recursive: true);
        }
        catch
        { /* test cleanup, swallow */
        }
    }

    private static void RestoreProject(string dest) =>
        RunDotnet(dest, $"restore \"{dest}\"", "dotnet restore");

    private static void RestoreLocalTools(string dest) =>
        RunDotnet(dest, "tool restore", "dotnet tool restore");

    private static void RunDotnet(string cwd, string args, string label)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("dotnet", args)
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{label} failed in '{cwd}' (exit {p.ExitCode}):\n{stdout}\n{stderr}"
            );
        }
    }

    private static void InstallLocalToolManifest(string dest, string repoRoot)
    {
        // Copy the repo's pinned dotnet-tools.json into the fixture temp dir so
        // `dotnet ef` resolves to the version we test against, not whatever
        // global tool happens to be on the developer's machine.
        var srcManifest = Path.Combine(repoRoot, ".config", "dotnet-tools.json");
        var destConfigDir = Path.Combine(dest, ".config");
        Directory.CreateDirectory(destConfigDir);
        File.Copy(srcManifest, Path.Combine(destConfigDir, "dotnet-tools.json"));
    }

    private static void WriteDirectoryBuildProps(string dest, string repoRoot)
    {
        // Emit a Directory.Build.props so fixture projects copied to temp dirs can
        // reference back to the repo using $(EmRepoRoot) in their ProjectReferences.
        // Also imports the package's buildTransitive targets so fixtures get the
        // [assembly: DesignTimeServicesReference] auto-injection that real NuGet
        // consumers would pick up via the package's buildTransitive flow.
        var buildProps = $"""
            <Project>
              <PropertyGroup>
                <EmRepoRoot>{repoRoot}</EmRepoRoot>
              </PropertyGroup>
              <Import Project="{repoRoot}/Directory.Build.props" />
              <Import Project="{repoRoot}/src/EntityFrameworkCore.ExtensibleMigrations/buildTransitive/EntityFrameworkCore.ExtensibleMigrations.targets" />
            </Project>
            """;
        File.WriteAllText(Path.Combine(dest, "Directory.Build.props"), buildProps);

        // CPM (Central Package Management) auto-discovers Directory.Packages.props by
        // walking up from the project directory. Copy it so it is found in the temp dir.
        File.Copy(
            Path.Combine(repoRoot, "Directory.Packages.props"),
            Path.Combine(dest, "Directory.Packages.props")
        );
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "ExtensibleMigrations.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir
            ?? throw new InvalidOperationException(
                "Repo root (ExtensibleMigrations.slnx) not found"
            );
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
            // skip Migrations — fixtures must scaffold from scratch each run
            var name = Path.GetFileName(dir);
            if (name is "bin" or "obj" or "Migrations")
                continue;
            CopyDirectory(dir, Path.Combine(dst, name));
        }
    }
}
