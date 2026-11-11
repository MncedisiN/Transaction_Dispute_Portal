using Dapper;
using DisputePortal.Api.Repository.Data;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Database_initialization_is_repeatable_and_preserves_existing_financial_rows()
    {
        var initializer = new DatabaseInitializer(
            _connections,
            DatabaseDialect.GetDatabaseDialect(_provider),
            DisputePortal.TestFixtures.TestRuntimeConfiguration.Read());
        await using var connection = await _connections.OpenConnection(default);
        var before = (await connection.QueryAsync<string>(
            "SELECT checksum FROM database_installation WHERE component = 'Api'")).Single();
        var count = await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM transactions.transactions");
        await initializer.InitializeDatabase(default);
        await initializer.InitializeDatabase(default);
        await initializer.ValidateSchema(default);
        Assert.Matches("^[0-9A-F]{64}$", before);
        Assert.Equal(before, await connection.QuerySingleAsync<string>(
            "SELECT checksum FROM database_installation WHERE component = 'Api'"));
        Assert.Equal(count, await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM transactions.transactions"));
    }

    [Fact]
    public async Task Changed_installation_checksum_blocks_startup_without_changing_financial_rows()
    {
        var initializer = new DatabaseInitializer(
            _connections,
            DatabaseDialect.GetDatabaseDialect(_provider),
            DisputePortal.TestFixtures.TestRuntimeConfiguration.Read());
        await using var connection = await _connections.OpenConnection(default);
        var count = await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM transactions.transactions");
        await connection.ExecuteAsync(
            "UPDATE database_installation SET checksum = @Checksum WHERE component = 'Api'",
            new { Checksum = new string('A', 64) });
        await Assert.ThrowsAsync<InvalidOperationException>(() => initializer.ValidateSchema(default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => initializer.InitializeDatabase(default));
        Assert.Equal(count, await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM transactions.transactions"));
    }
}
