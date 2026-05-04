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

    [Fact]
    public void AssertMatches_writes_golden_and_throws_on_first_run()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"golden-{Guid.NewGuid():N}.cs");
        try
        {
            var ex = Assert.Throws<Xunit.Sdk.XunitException>(() =>
                MigrationGoldenFile.AssertMatches("body\n", tmp)
            );
            Assert.Contains("did not exist", ex.Message);
            Assert.True(File.Exists(tmp));
        }
        finally
        {
            if (File.Exists(tmp))
                File.Delete(tmp);
        }
    }

    [Fact]
    public void AssertMatches_passes_when_content_matches()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"golden-{Guid.NewGuid():N}.cs");
        try
        {
            File.WriteAllText(tmp, "body\n");
            MigrationGoldenFile.AssertMatches("body\n", tmp); // should not throw
        }
        finally
        {
            if (File.Exists(tmp))
                File.Delete(tmp);
        }
    }

    [Fact]
    public void AssertMatches_throws_with_diff_on_mismatch()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"golden-{Guid.NewGuid():N}.cs");
        try
        {
            File.WriteAllText(tmp, "expected\n");
            var ex = Assert.Throws<Xunit.Sdk.XunitException>(() =>
                MigrationGoldenFile.AssertMatches("actual\n", tmp)
            );
            Assert.Contains("Golden mismatch", ex.Message);
            Assert.Contains("expected", ex.Message);
            Assert.Contains("actual", ex.Message);
        }
        finally
        {
            if (File.Exists(tmp))
                File.Delete(tmp);
        }
    }
}
