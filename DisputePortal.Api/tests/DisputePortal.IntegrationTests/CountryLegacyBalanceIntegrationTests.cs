using System.Security.Claims;
using Dapper;
using DisputePortal.Api.Repository;
using Xunit;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Models;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    private async Task AppendCountryLegacyAdjustment(decimal current, decimal reserved = 0, int unknown = 0)
    {
        await using var connection = await _connections.OpenConnection(default);
        await connection.ExecuteAsync(
            """
            INSERT INTO "ledger"."projectionEntries" (
                "accountId",
                "transactionId",
                "currentDelta",
                "reservedDelta",
                "unknownDelta",
                "eventKind")
            SELECT payment."accountId",
                payment.id,
                @Current,
                @Reserved,
                @Unknown,
                'Correction'
            FROM "transactions"."transactions" payment
            WHERE payment."publicId" = @Payment
            """,
            new
            {
                Current = current,
                Reserved = reserved,
                Unknown = unknown,
                Payment = Transaction
            });
    }

    [Theory]
    [InlineData("Balance")]
    [InlineData("Reserve")]
    [InlineData("UnknownJournal")]
    [InlineData("UnknownExternalBank")]
    public async Task Country_probe_blocks_unimported_legacy_balances_reserves_and_unknown_books(string reason)
    {
        var ct = CancellationToken.None;
        var original = Assert.Single(await Finance().GetBalances(Customer, ct));
        Assert.True(original.BalanceKnown);
        Assert.NotEqual(0m, original.CurrentBalance);
        if (reason != "Balance")
        {
            await AppendCountryLegacyAdjustment(
                -original.CurrentBalance!.Value,
                reason == "Reserve" ? 17m : 0m,
                reason == "UnknownJournal" ? 1 : 0);
            if (reason == "UnknownExternalBank")
            {
                await _repository.LinkOwnedAccount(Customer, Account, "country-probe-bank", "country-probe-external", ct);
            }
        }

        var client = new ImportedCountryAccountsClient([OtherAccount]);
        var blocked = await new CountryPolicyEligibilityReader(_connections, CountryProbeDatabase(), client).GetCountryChangeBlockers(ct);
        Assert.False(blocked.Allowed);
        Assert.Equal(reason == "Balance", blocked.HasNonzeroBalances);
        Assert.Equal(reason == "Reserve", blocked.HasReserves);
        Assert.Equal(reason.StartsWith("Unknown", StringComparison.Ordinal), blocked.HasUnfinishedOperations);
        Assert.False(blocked.HasPendingRefunds);
        Assert.Contains("reconciled and settled", blocked.Blocker);
        var sent = client.Requests.SelectMany(batch => batch).ToArray();
        Assert.Contains(Account, sent);
        Assert.Contains(OtherAccount, sent);
        // The same local journal is a historical snapshot after native import.
        // Ledger's own eligibility probe now owns those live balances.
        var importedClient = new ImportedCountryAccountsClient();
        var imported = await new CountryPolicyEligibilityReader(_connections, CountryProbeDatabase(), importedClient).GetCountryChangeBlockers(ct);
        Assert.True(imported.Allowed);
        Assert.False(imported.HasNonzeroBalances);
        Assert.False(imported.HasReserves);
        Assert.False(imported.HasUnfinishedOperations);
        Assert.Null(imported.Blocker);
        Assert.NotEmpty(importedClient.Requests);
    }

    [Fact]
    public async Task Country_probe_fails_closed_when_legacy_balances_cannot_be_compared_with_canonical_imports()
    {
        var error = await Assert.ThrowsAsync<PlatformException>(() => new CountryPolicyEligibilityReader(
            _connections,
            CountryProbeDatabase()).GetCountryChangeBlockers(CancellationToken.None));
        Assert.Equal(503, error.Status);
        Assert.Equal("country_balances_unavailable", error.Code);
        Assert.Equal("Account balances could not be verified. Try again shortly.", error.Message);
    }

    private sealed class ImportedCountryAccountsClient(Guid[]? imported = null) : IServiceClient
    {
        public List<Guid[]> Requests { get; } = [];

        public Task<T> SendServiceRequest<T>(
            string service,
            HttpMethod method,
            string path,
            object? body = null,
            ClaimsPrincipal? actor = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Assert.Equal("ledger", service);
            Assert.Equal(HttpMethod.Post, method);
            Assert.Equal("/internal/book-imports/imported-accounts", path);
            var requested = Assert.IsType<Guid[]>(body);
            Assert.InRange(requested.Length, 1, 500);
            Requests.Add(requested);
            return Task.FromResult((T)(object)(imported is null ? requested : requested.Intersect(imported).ToArray()));
        }
    }
}
