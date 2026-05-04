namespace EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Harness;

public sealed record DotnetEfResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Succeeded => ExitCode == 0;

    public string CombinedOutput =>
        string.IsNullOrEmpty(StdErr) ? StdOut : $"{StdOut}\n--- STDERR ---\n{StdErr}";

    public void EnsureSuccess()
    {
        if (Succeeded)
            return;
        throw new InvalidOperationException(
            $"dotnet ef failed with exit code {ExitCode}.\n{CombinedOutput}"
        );
    }
}
