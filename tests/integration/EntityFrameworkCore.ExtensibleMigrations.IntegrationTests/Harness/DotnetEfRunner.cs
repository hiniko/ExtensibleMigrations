using System.Diagnostics;
using System.Text;

namespace EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Harness;

/// <summary>
/// Runs <c>dotnet ef</c> commands against a project directory. The connection
/// string (when supplied) is exposed via the <c>INTEGRATION_PG_CONNECTION</c>
/// environment variable; fixture DbContexts read the same env var.
/// </summary>
public sealed class DotnetEfRunner(string projectDir, IDictionary<string, string>? env = null)
{
    private readonly IDictionary<string, string> _env = env ?? new Dictionary<string, string>();

    public Task<DotnetEfResult> AddMigrationAsync(string name, string? connectionString = null) =>
        RunAsync($"ef migrations add {name} --project \"{projectDir}\"", connectionString);

    public Task<DotnetEfResult> UpdateDatabaseAsync(string connectionString) =>
        RunAsync($"ef database update --project \"{projectDir}\"", connectionString);

    /// <summary>
    /// Rolls forward or back to a specific migration. Pass <c>"0"</c> to revert all.
    /// </summary>
    public Task<DotnetEfResult> UpdateDatabaseAsync(
        string targetMigration,
        string connectionString
    ) =>
        RunAsync(
            $"ef database update {targetMigration} --project \"{projectDir}\"",
            connectionString
        );

    public Task<DotnetEfResult> GenerateScriptAsync(
        string connectionString,
        string outputFile,
        bool idempotent = true
    ) =>
        RunAsync(
            $"ef migrations script --output \"{outputFile}\" --project \"{projectDir}\""
                + (idempotent ? " --idempotent" : ""),
            connectionString
        );

    public Task<DotnetEfResult> RemoveLastMigrationAsync(string? connectionString = null) =>
        RunAsync($"ef migrations remove --project \"{projectDir}\" --force", connectionString);

    private async Task<DotnetEfResult> RunAsync(string args, string? connectionString)
    {
        var psi = new ProcessStartInfo("dotnet", args)
        {
            WorkingDirectory = projectDir,
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
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stdout.AppendLine(e.Data);
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stderr.AppendLine(e.Data);
        };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        await p.WaitForExitAsync();
        return new DotnetEfResult(p.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
