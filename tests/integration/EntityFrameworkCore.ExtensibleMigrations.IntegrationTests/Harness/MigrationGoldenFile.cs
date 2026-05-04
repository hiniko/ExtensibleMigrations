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
        @"\[Migration\(""\d{14}_",
        RegexOptions.Compiled
    );

    private static readonly Regex MigrationFilenameRegex = new(@"^\d{14}_", RegexOptions.Compiled);

    public static void AssertMatches(string actualContent, string goldenPath)
    {
        if (!File.Exists(goldenPath))
        {
            // First run: write actual as the new golden. Engineer must commit + review.
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllText(goldenPath, Normalise(actualContent));
            throw new Xunit.Sdk.XunitException(
                $"Golden file did not exist; wrote {goldenPath}. Inspect, then commit if correct."
            );
        }

        var actualNormalised = Normalise(actualContent);
        var goldenNormalised = Normalise(File.ReadAllText(goldenPath));

        var actualHash = Sha256(actualNormalised);
        var goldenHash = Sha256(goldenNormalised);

        if (actualHash == goldenHash)
            return;

        // Mismatch: write actual to /tmp and emit a unified diff line-by-line.
        var actualOut = Path.Combine(Path.GetTempPath(), $"actual-{Path.GetFileName(goldenPath)}");
        File.WriteAllText(actualOut, actualNormalised);
        throw new Xunit.Sdk.XunitException(
            $"Golden mismatch.\n  Expected SHA: {goldenHash}\n  Actual SHA:   {actualHash}\n"
                + $"  Golden:  {goldenPath}\n  Actual:  {actualOut}\n"
                + $"--- diff ---\n{Diff(goldenNormalised, actualNormalised)}"
        );
    }

    public static string Normalise(string content)
    {
        // Strip EF timestamp inside [Migration("20240101000000_Name")] -> [Migration("_Name")]
        var stripped = MigrationTimestampRegex.Replace(content, @"[Migration(""_");
        // CRLF -> LF
        stripped = stripped.Replace("\r\n", "\n");
        // Trailing whitespace per line
        var lines = stripped.Split('\n').Select(l => l.TrimEnd());
        return string.Join('\n', lines);
    }

    public static bool IsGeneratedMigrationFile(string fileName) =>
        MigrationFilenameRegex.IsMatch(Path.GetFileName(fileName));

    private static readonly Regex EmptyUpBodyRegex = new(
        @"protected\s+override\s+void\s+Up\s*\(\s*MigrationBuilder\s+migrationBuilder\s*\)\s*\{\s*\}",
        RegexOptions.Compiled
    );

    private static readonly Regex EmptyDownBodyRegex = new(
        @"protected\s+override\s+void\s+Down\s*\(\s*MigrationBuilder\s+migrationBuilder\s*\)\s*\{\s*\}",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Asserts a re-scaffolded migration has empty Up and Down bodies — the
    /// scaffold-after-apply roundtrip check that verifies the snapshot is
    /// identical to the model.
    /// </summary>
    public static void AssertEmptyMigrationBody(string content)
    {
        var normalised = Normalise(content);
        if (!EmptyUpBodyRegex.IsMatch(normalised))
        {
            throw new Xunit.Sdk.XunitException(
                $"Expected empty Up() body but found differences. Migration:\n{normalised}"
            );
        }
        if (!EmptyDownBodyRegex.IsMatch(normalised))
        {
            throw new Xunit.Sdk.XunitException(
                $"Expected empty Down() body but found differences. Migration:\n{normalised}"
            );
        }
    }

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
            if (el == al)
                continue;
            sb.AppendLine($"L{i + 1, 4} - {el}");
            sb.AppendLine($"L{i + 1, 4} + {al}");
        }
        return sb.ToString();
    }
}
