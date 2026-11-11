using DisputePortal.TestFixtures;
using System.Data.Common;
using System.Security.Claims;
using Dapper;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Platform;
using DisputePortal.Api.Repository;
using DisputePortal.Api.Repository.Data;
using DisputePortal.Api.Service.Disputes;
using DisputePortal.Platform.Contracts;
using Xunit;
using DisputePortal.Platform.Infrastructure.Interfaces;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    private async Task MarkFinancialClaimAccountMigrated(Guid account)
    {
        await using var connection = await _connections.OpenConnection(default);
        await connection.ExecuteAsync(
            """
            INSERT INTO "integration"."accountMigrations" (
                "accountId",
                "snapshotId",
                "sourceHash",
                "countryCode",
                "state",
                "capturedAt",
                "activatedAt")
            SELECT account.id,
                @Snapshot,
                @Hash,
                'ZA',
                'Active',
                @Now,
                @Now
            FROM "accounts"."accounts" account
            WHERE account."publicId" = @Account
            """,
            new
            {
                Account = account,
                Snapshot = Guid.NewGuid(),
                Hash = new string('f', 64),
                Now = DateTimeOffset.UtcNow
            });
    }

    [Theory]
    [InlineData("Approval", false)]
    [InlineData("Approval", true)]
    [InlineData("Recall", false)]
    [InlineData("Automatic", false)]
    public async Task Rolled_back_financial_request_releases_its_country_claim_without_masking_the_original_failure(
        string flow,
        bool unavailable)
    {
        var ct = CancellationToken.None;
        Guid account = Account;
        Guid dispute;
        int version;
        var request = Guid.NewGuid();
        if (flow == "Automatic")
        {
            await VerifyProvisionedContact(Customer, "customer@test.local");
            var opened = await _repository.OpenAccount(Customer, Guid.NewGuid(), "Savings", 10, ct);
            account = opened.AccountId;
            await _repository.ProcessNextTransactionBatch(ct);
            await using var lookup = await _connections.OpenConnection(ct);
            var transaction = await lookup.ExecuteScalarAsync<Guid>(
                """
                SELECT payment."publicId"
                FROM "transactions"."duplicateConfirmations" proof
                JOIN "transactions"."transactions" payment ON payment.id = proof."transactionId"
                JOIN "accounts"."accounts" account ON account.id = payment."accountId"
                WHERE account."publicId" = @Account
                """,
                new { Account = account });
            dispute = (await _repository.CreateDispute(
                Customer,
                new CreateDisputeCommand
                {
                    TransactionId = transaction,
                    Category = "Duplicate",
                    Reason = "I was charged more than once",
                    Description = "Review this confirmed duplicate merchant payment."
                },
                "claim-release-auto",
                ct)).Id;
            version = (await _repository.GetCustomerDispute(Customer, dispute, ct))!.Dispute.Version;
        }
        else
        {
            var reviewed = await ReviewableRefundCase();
            dispute = reviewed.Id;
            version = reviewed.Version;
            if (flow == "Recall")
            {
                await FundRecallAccount();
                await Finance().ApproveRefund(
                    Agent,
                    dispute,
                    new(version, Guid.NewGuid(), "Approve the verified original merchant refund."),
                    "claim-release-prepare",
                    ct);
                version = (await _repository.GetCustomerDispute(Customer, dispute, ct))!.Dispute.Version;
            }
        }

        await MarkFinancialClaimAccountMigrated(account);
        var client = new FinancialCountryClaimClient(account)
        {
            RejectSettlement = unavailable
        };
        var financialCommands = new DisputeFinancialCommandService(client, DatabaseDialect.GetDatabaseDialect(_provider));
        var finance = new FinanceRepository(
            _connections,
            DatabaseDialect.GetDatabaseDialect(_provider),
            TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"),
            TestRuntimeConfiguration.Read(),
            new CaseServicePolicy(),
            financialCommands: financialCommands);
        await using var connection = await _connections.OpenConnection(ct);
        await connection.ExecuteAsync("""
            ALTER TABLE "disputes"."financialCommands"
            ADD CONSTRAINT ck_test_reject_financial_command CHECK (amount < 0)
            """);
        Task Invoke() => flow switch
        {
            "Approval" => finance.ApproveRefund(
            Agent,
            dispute,
            new(version, request, "Approve the original payment after a complete review."),
            "claim-release",
            ct),
            "Recall" => finance.RecallRefund(
            Agent,
            dispute,
            new(version, request, "Recall the approved refund after reviewing merchant evidence."),
            "claim-release",
            ct),
            _ => finance.ProcessAutomatic(ct)
        };
        try
        {
            var error = await Assert.ThrowsAnyAsync<DbException>(Invoke);
            Assert.Contains("ck_test_reject_financial_command", error.Message, StringComparison.OrdinalIgnoreCase);
            var claim = Assert.Single(client.Claims);
            Assert.Equal(claim.Key, Assert.Single(client.SettlementAttempts));
            if (flow != "Automatic")
            {
                Assert.NotEqual(request, claim.Key);
            }

            Assert.Equal(!unavailable, client.Settled.Contains(claim.Key));
            Assert.Equal(
                0,
                await connection.ExecuteScalarAsync<int>(
                    """
                SELECT COUNT(*)
                FROM "disputes"."financialCommands"
                WHERE "requestId" = @Request
                """,
                    new { Request = request }));
            Assert.Equal(
                0,
                await connection.ExecuteScalarAsync<int>("""
                SELECT COUNT(*)
                FROM service_outbox
                WHERE event_type = 'dispute.financial.requested'
                """));
            var unchanged = (await _repository.GetCustomerDispute(Customer, dispute, ct))!.Dispute;
            Assert.Equal(version, unchanged.Version);
            Assert.Equal(flow == "Recall" ? "Resolved" : flow == "Automatic" ? "Submitted" : "UnderReview", unchanged.Status);
            if (flow == "Automatic")
            {
                Assert.Equal(
                    0,
                    await connection.ExecuteScalarAsync<int>("""
                    SELECT COUNT(*)
                    FROM "disputes"."automaticDecisions"
                    """));
            }
        }
        finally
        {
            await connection.ExecuteAsync("""
                ALTER TABLE "disputes"."financialCommands"
                DROP CONSTRAINT ck_test_reject_financial_command
                """);
        }

        if (!unavailable)
        {
            await Invoke();
            Assert.Equal(2, client.Claims.Count);
            Assert.Equal(2, client.Claims.Values.Select(value => value.OperationId).Distinct().Count());
            Assert.Single(client.Settled);
            var committedRequest = await connection.ExecuteScalarAsync<Guid>("""
                SELECT "requestId"
                FROM "disputes"."financialCommands"
                """);
            var committedIdentity = await connection.ExecuteScalarAsync<Guid>("""
                SELECT "publicId"
                FROM "disputes"."financialCommands"
                """);
            if (flow != "Automatic")
            {
                Assert.Equal(request, committedRequest);
                await Invoke();
            }
            else
            {
                Assert.False(await finance.ProcessAutomatic(ct));
            }

            Assert.Equal(2, client.Claims.Count);
            Assert.Equal(
                committedIdentity,
                await connection.ExecuteScalarAsync<Guid>("""
                SELECT "publicId"
                FROM "disputes"."financialCommands"
                """));
            await financialCommands.ReleaseUncommitted(connection, committedRequest, client.Claims.Last().Key, ct);
            Assert.Single(client.SettlementAttempts);
        }
    }

    [Theory]
    [InlineData("Queued")]
    [InlineData("Posted")]
    [InlineData("Failed")]
    public async Task Country_claim_cleanup_never_settles_a_persisted_financial_request(string state)
    {
        var ct = CancellationToken.None;
        var command = await QueueCountryProbeCommand("Refund", ct);
        await using var connection = await _connections.OpenConnection(ct);
        var request = await connection.ExecuteScalarAsync<Guid>(
            """
            SELECT "requestId"
            FROM "disputes"."financialCommands"
            WHERE "publicId" = @Command
            """,
            new { Command = command });
        if (state != "Queued")
        {
            await connection.ExecuteAsync(
                """
                UPDATE "disputes"."financialCommands"
                SET state = @State,
                    "completedAt" = @Now
                WHERE "publicId" = @Command
                """,
                new
                {
                    State = state,
                    Now = DateTimeOffset.UtcNow,
                    Command = command
                });
        }

        var client = new FinancialCountryClaimClient(Account);
        await new DisputeFinancialCommandService(client, DatabaseDialect.GetDatabaseDialect(_provider)).ReleaseUncommitted(
            connection,
            request,
            Guid.NewGuid(),
            ct);
        Assert.Empty(client.SettlementAttempts);
    }

    [Fact]
    public async Task Uncommitted_claim_absence_check_and_settlement_keep_the_existing_operation_fence()
    {
        var ct = CancellationToken.None;
        var client = new FinancialCountryClaimClient(Account)
        {
            SettlementEntered = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ContinueSettlement = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await using var connection = await _connections.OpenConnection(ct);
        var release = new DisputeFinancialCommandService(client, DatabaseDialect.GetDatabaseDialect(_provider)).ReleaseUncommitted(
            connection,
            Guid.NewGuid(),
            Guid.NewGuid(),
            ct);
        try
        {
            await client.SettlementEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var blocked = await CountryProbe().GetCountryChangeBlockers(ct);
            Assert.True(blocked.HasUnfinishedOperations);
            Assert.Contains("in progress", blocked.Blocker);
        }
        finally
        {
            client.ContinueSettlement.TrySetResult();
            await release;
        }

        Assert.Single(client.Settled);
        Assert.False((await CountryProbe().GetCountryChangeBlockers(ct)).HasUnfinishedOperations);
    }

    private sealed class FinancialCountryClaimClient(Guid account) : IServiceClient
    {
        public Dictionary<Guid, CountryOperation> Claims { get; } = [];
        public List<Guid> SettlementAttempts { get; } = [];
        public HashSet<Guid> Settled { get; } = [];
        public bool RejectSettlement { get; set; }
        public TaskCompletionSource? SettlementEntered { get; set; }
        public TaskCompletionSource? ContinueSettlement { get; set; }

        public async Task<T> SendServiceRequest<T>(
            string service,
            HttpMethod method,
            string path,
            object? body = null,
            ClaimsPrincipal? actor = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            object result;
            if (service == "accounts" && method == HttpMethod.Get && path == "/internal/accounts/" + account)
            {
                result = new AccountRecord
                {
                    Id = account,
                    UserId = Customer,
                    Number = "fixture-account",
                    Type = "Savings",
                    CountryCode = "ZA",
                    Currency = "ZAR",
                    Status = "Active",
                    LedgerBalance = 0,
                    ReservedBalance = 0,
                    Version = 1,
                    CreatedAt = DateTimeOffset.UtcNow
                };
            }
            else if (service == "configuration"
                && method == HttpMethod.Post
                && path == "/country-operations"
                && body is CountryOperationRequest request)
            {
                Assert.Equal("DisputeFinance", request.SourceKind);
                if (!Claims.TryGetValue(request.RequestId, out var operation))
                {
                    operation = new CountryOperation
                    {
                        RequestId = request.RequestId,
                        OperationId = request.OperationId ?? Guid.NewGuid(),
                        SourceKind = request.SourceKind,
                        CountryCode = request.CountryCode,
                        Currency = request.Currency,
                        OriginalTransactionId = request.OriginalTransactionId,
                        PolicyVersion = 1
                    };
                    Claims.Add(request.RequestId, operation);
                }

                result = operation;
            }
            else if (service == "configuration"
                && method == HttpMethod.Post
                && path.StartsWith("/country-operations/DisputeFinance/", StringComparison.Ordinal)
                && path.EndsWith("/settle", StringComparison.Ordinal))
            {
                var settlementRequest = Guid.Parse(path.Split('/')[3]);
                SettlementAttempts.Add(settlementRequest);
                SettlementEntered?.TrySetResult();
                if (ContinueSettlement is not null)
                {
                    await ContinueSettlement.Task.WaitAsync(ct);
                }

                if (RejectSettlement)
                {
                    throw new HttpRequestException("Isolated bookkeeping outage.");
                }

                Settled.Add(settlementRequest);
                result = new object();
            }
            else
            {
                throw new InvalidOperationException("Unexpected isolated financial claim request.");
            }

            return (T)result;
        }
    }
}
