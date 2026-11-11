using DisputePortal.TestFixtures;
using System.Security.Claims;
using Dapper;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Platform;
using DisputePortal.Api.Repository;
using DisputePortal.Api.Repository.Data;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using Xunit;
using DisputePortal.Platform.Infrastructure.Interfaces;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    private static PlatformSettings HistoricalSettlementSettings(string operatingCountry) =>
        new global::DisputePortal.Platform.Contracts.PlatformSettings
    {
        Version = 3,
        OperatingCountry = operatingCountry,
        Branding = new global::DisputePortal.Platform.Contracts.BrandingDefinition
        {
            Name = "Dispute Portal",
            AccentColor = "#00577D",
            LogoColor = "#EE3124"
        },
        Registration = new global::DisputePortal.Platform.Contracts.RegistrationRequirements
        {
            RequireEmail = true,
            RequirePhone = true,
            RequireDocument = true,
            RequireAddress = true,
            RequireVerification = true
        },
        Currencies = [new global::DisputePortal.Platform.Contracts.CurrencyDefinition
        {
            Code = "ZAR",
            Name = "South African Rand",
            Symbol = "R",
            Decimals = 2
        }, new global::DisputePortal.Platform.Contracts.CurrencyDefinition
        {
            Code = "ZWG",
            Name = "ZiG",
            Symbol = "ZiG",
            Decimals = 2
        }

        ],
        Countries = [new global::DisputePortal.Platform.Contracts.CountryDefinition
        {
            Code = "ZA",
            Name = "South Africa",
            Flag = "ZA",
            DialCode = "+27",
            Locale = "en-ZA",
            TimeZone = "Africa/Johannesburg",
            DefaultCurrency = "ZAR",
            Currencies = ["ZAR"],
            Documents = [],
            Regions = [],
            RegionLabel = "Province",
            PostalLabel = "Postal Code",
            PostalRequired = true
        }, new global::DisputePortal.Platform.Contracts.CountryDefinition
        {
            Code = "ZW",
            Name = "Zimbabwe",
            Flag = "ZW",
            DialCode = "+263",
            Locale = "en-ZW",
            TimeZone = "Africa/Harare",
            DefaultCurrency = "ZWG",
            Currencies = ["ZWG"],
            Documents = [],
            Regions = [],
            RegionLabel = "Province",
            PostalLabel = "Postal Code",
            PostalRequired = false
        }

        ],
        FinancialRules = []
    };

    private async Task BlockHistoricalAccount(CancellationToken ct)
    {
        await using var connection = await _connections.OpenConnection(ct);
        await connection.ExecuteAsync(
            """
            UPDATE "accounts"."accounts"
            SET "accountStatus"='Blocked'
            WHERE "publicId"=@Account
            """,
            new { Account });
    }

    private async Task FinalizeHistoricalCommand(FinanceRepository finance, Guid transaction, CancellationToken ct)
    {
        await using var connection = await _connections.OpenConnection(ct);
        var command = await connection.QuerySingleAsync<DisputeFinancialCommand>(
            """
            SELECT command."publicId" AS TransactionId,
                owner."publicId" AS UserId,
                account."publicId" AS AccountId,
                command.kind AS Kind,
                command.amount AS Amount,
                command."currencyCode" AS Currency,
                command.correlation AS Correlation
            FROM "disputes"."financialCommands" AS command
            JOIN "identity"."users" AS owner ON owner.id=command."customerId"
            JOIN "accounts"."accounts" AS account ON account.id=command."accountId"
            WHERE command."publicId"=@Id
            """,
            new { Id = transaction });
        var completed = DomainEvent.CreateDomainEvent(
            "transactions",
            EventTypes.TransactionCompleted,
            new TransactionEvent
            {
                TransactionId = command.TransactionId,
                PaymentId = command.TransactionId,
                UserId = command.UserId,
                Kind = command.Kind,
                Reference = "ISOLATED-HISTORICAL-OUTCOME",
                AccountId = command.AccountId,
                RecipientAccountId = null,
                Amount = command.Amount,
                Currency = command.Currency,
                TargetAmount = null,
                TargetCurrency = null,
                Fee = 0,
                Status = "Completed"
            },
            command.Correlation);
        await using var transactionScope = await connection.BeginTransactionAsync(ct);
        await RelationalDialect.LockOperations(
            connection,
            transactionScope,
            DatabaseDialect.GetDatabaseDialect(_provider).OperationsLockSql,
            ct);
        var projectionDialect = RelationalDialect.GetDatabaseDialect(_provider.ToString());
        await connection.ExecuteAsync(new CommandDefinition(
            projectionDialect.EnableFinancialProjectionSql,
            transaction: transactionScope,
            cancellationToken: ct));
        Exception? projectionFailure = null;
        try
        {
            await finance.ApplyFinancialOutcome(connection, transactionScope, completed, ct);
        }
        catch (Exception error)
        {
            projectionFailure = error;
            throw;
        }
        finally
        {
            // Match the real signed-event projection boundary; never weaken its database guard.
            if (projectionFailure is null || projectionDialect is not PostgreSqlDialect)
            {
                try
                {
                    await connection.ExecuteAsync(new CommandDefinition(
                        projectionDialect.DisableFinancialProjectionSql,
                        transaction: transactionScope.Connection is null ? null : transactionScope,
                        cancellationToken: CancellationToken.None));
                }
                catch
                {
                    if (connection is Microsoft.Data.SqlClient.SqlConnection sql)
                    {
                        Microsoft.Data.SqlClient.SqlConnection.ClearPool(sql);
                    }

                    if (projectionFailure is null)
                    {
                        throw;
                    }
                }
            }
        }

        await transactionScope.CommitAsync(ct);
    }

    [Fact]
    public async Task Admin_can_settle_and_recall_a_quarantined_original_currency_without_reactivating_or_cloning_the_debit()
    {
        var ct = CancellationToken.None;
        var reviewed = await ReviewableRefundCase();
        await GrantRefundSupervisor(OtherCustomer, "super_admin", ct);
        await MarkFinancialClaimAccountMigrated(Account);
        await BlockHistoricalAccount(ct);
        var client = new HistoricalSettlementClient();
        var finance = new FinanceRepository(
            _connections,
            DatabaseDialect.GetDatabaseDialect(_provider),
            TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"),
            TestRuntimeConfiguration.Read(),
            new CaseServicePolicy(),
            financialCommands: new DisputeFinancialCommandService(client, DatabaseDialect.GetDatabaseDialect(_provider)));
        Assert.True((await finance.GetCaseFinancials(OtherCustomer, reviewed.Id, ct)).CanApproveRefund);
        Assert.False((await finance.GetCaseFinancials(Customer, reviewed.Id, ct)).CanApproveRefund);
        var request = new ApproveRefundRequest(
            reviewed.Version,
            Guid.NewGuid(),
            "Approve the exact original debit in its historical currency after specialist review.");
        var refund = await finance.ApproveRefund(OtherCustomer, reviewed.Id, request, "blocked-historical-refund", ct);
        Assert.Equal("Processing", refund.Status);
        Assert.False(refund.Automatic);
        Assert.Equal("ZAR", refund.Currency);
        Assert.Equal(100m, refund.Amount);
        Assert.Equal(Transaction, Assert.Single(client.Finance.Claims.Values).OriginalTransactionId);
        await FinalizeHistoricalCommand(finance, refund.CreditTransactionId, ct);
        Assert.True((await finance.ApproveRefund(OtherCustomer, reviewed.Id, request, "blocked-historical-replay", ct)).Replayed);
        var closed = (await _repository.GetCustomerDispute(Customer, reviewed.Id, ct))!.Dispute;
        Assert.True((await finance.GetCaseFinancials(OtherCustomer, reviewed.Id, ct)).CanRecall);
        Assert.False((await finance.GetCaseFinancials(Customer, reviewed.Id, ct)).CanReinstate);
        var recall = await finance.RecallRefund(
            OtherCustomer,
            reviewed.Id,
            new RecallRefundRequest(closed.Version, Guid.NewGuid(), "Recall the historical refund after reviewing new merchant evidence."),
            "blocked-historical-recall",
            ct);
        Assert.Equal("ZAR", recall.Currency);
        Assert.Equal(100m, recall.Amount);
        Assert.Contains(client.Finance.Claims.Values, claim => claim.OriginalTransactionId == refund.CreditTransactionId);
        await FinalizeHistoricalCommand(finance, recall.ReversalTransactionId, ct);
        await using var connection = await _connections.OpenConnection(ct);
        Assert.Equal(
            "Blocked",
            await connection.ExecuteScalarAsync<string>(
                """
            SELECT "accountStatus"
            FROM "accounts"."accounts"
            WHERE "publicId"=@Account
            """,
                new { Account }));
        Assert.Equal(
            1,
            await connection.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "transactions"."transactions"
            WHERE "publicId"=@Transaction
                AND "currencyCode"='ZAR' AND amount=100 AND direction='Debit'
            """,
                new { Transaction }));
        Assert.Equal(
            2,
            await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "disputes"."financialCommands"
            WHERE state='Posted' AND "currencyCode"='ZAR' AND amount=100
            """));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM \"disputes\".\"refunds\""));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM \"disputes\".\"refundRecalls\""));
    }

    [Theory]
    [InlineData("CurrentCurrency")]
    [InlineData("DisabledCustomer")]
    [InlineData("WrongRemoteOwner")]
    [InlineData("RemoteActive")]
    public async Task Historical_exception_does_not_authorize_current_currency_blocks_disabled_customers_or_another_owner(string rejection)
    {
        var ct = CancellationToken.None;
        var reviewed = await ReviewableRefundCase();
        await GrantRefundSupervisor(OtherCustomer, "super_admin", ct);
        await MarkFinancialClaimAccountMigrated(Account);
        await BlockHistoricalAccount(ct);
        var client = new HistoricalSettlementClient();
        if (rejection == "CurrentCurrency")
        {
            client.OperatingCountry = "ZA";
        }

        if (rejection == "WrongRemoteOwner")
        {
            client.Owner = OtherCustomer;
        }

        if (rejection == "RemoteActive")
        {
            client.Status = "Active";
        }

        if (rejection == "DisabledCustomer")
        {
            await using var disable = await _connections.OpenConnection(ct);
            await disable.ExecuteAsync(
                """
                UPDATE "identity"."users"
                SET "accountStatus"='Disabled'
                WHERE "publicId"=@Customer
                """,
                new { Customer });
        }

        var finance = new FinanceRepository(
            _connections,
            DatabaseDialect.GetDatabaseDialect(_provider),
            TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"),
            TestRuntimeConfiguration.Read(),
            new CaseServicePolicy(),
            financialCommands: new DisputeFinancialCommandService(client, DatabaseDialect.GetDatabaseDialect(_provider)));
        Assert.False((await finance.GetCaseFinancials(OtherCustomer, reviewed.Id, ct)).CanApproveRefund);
        await Assert.ThrowsAsync<PortalConflictException>(() => finance.ApproveRefund(
            OtherCustomer,
            reviewed.Id,
            new ApproveRefundRequest(
                reviewed.Version,
                Guid.NewGuid(),
                "A quarantined account requires the exact eligible historical settlement path."),
            "blocked-ineligible-refund",
            ct));
        Assert.Empty(client.Finance.Claims);
        await using var connection = await _connections.OpenConnection(ct);
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM \"disputes\".\"financialCommands\""));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM \"disputes\".\"refunds\""));
    }

    private sealed class HistoricalSettlementClient : IServiceClient
    {
        public FinancialCountryClaimClient Finance { get; } = new(Account);
        public Guid Owner { get; set; } = Customer;
        public string Status { get; set; } = "Blocked";
        public string OperatingCountry { get; set; } = "ZW";
        public int PolicyChecks { get; private set; }

        public Task<T> SendServiceRequest<T>(
            string service,
            HttpMethod method,
            string path,
            object? body = null,
            ClaimsPrincipal? actor = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (service == "accounts" && method == HttpMethod.Get && path == "/internal/accounts/" + Account)
            {
                return Task.FromResult((T)(object)new AccountRecord
                {
                    Id = Account,
                    UserId = Owner,
                    Number = "fixture-account",
                    Type = "Savings",
                    CountryCode = "ZA",
                    Currency = "ZAR",
                    Status = Status,
                    LedgerBalance = 0,
                    ReservedBalance = 0,
                    Version = 1,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }

            if (service == "configuration" && method == HttpMethod.Get && path == "/settings")
            {
                PolicyChecks++;
                return Task.FromResult((T)(object)HistoricalSettlementSettings(OperatingCountry));
            }

            return Finance.SendServiceRequest<T>(service, method, path, body, actor, ct);
        }
    }
}
