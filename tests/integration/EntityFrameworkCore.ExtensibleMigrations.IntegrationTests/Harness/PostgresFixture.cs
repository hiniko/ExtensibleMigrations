using Testcontainers.PostgreSql;

namespace EntityFrameworkCore.ExtensibleMigrations.IntegrationTests.Harness;

/// <summary>
/// xUnit collection fixture: spins up one Postgres 16 container for the test run.
/// Each test grabs a fresh database via <see cref="CreateDatabaseAsync"/>.
/// Docker must be running — Docker absence is a test failure, not a skip.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(
        "postgres:16-alpine"
    ).Build();

    public string AdminConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>
    /// Creates a new database with a unique name and returns its connection string.
    /// </summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var dbName = $"em_{Guid.NewGuid():N}";
        await using var conn = new Npgsql.NpgsqlConnection(AdminConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
        await cmd.ExecuteNonQueryAsync();

        var builder = new Npgsql.NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Database = dbName,
        };
        return builder.ConnectionString;
    }
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }
