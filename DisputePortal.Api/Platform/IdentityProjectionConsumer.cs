using DisputePortal.Api.Repository;
using System.Data.Common;
using Dapper;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Api.Interfaces.Finance;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Security;
using DisputePortal.Api.Interfaces.Services;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Platform;

public class IdentityProjectionConsumer(
    ServiceDatabase db,
    IServiceScopeFactory scopes,
    IServiceClient client,
    ILogger<IdentityProjectionConsumer>? logger = null) : IDomainEventConsumer
{
    public string[] Subscriptions => [EventTypes.AssessmentAccountRequested, EventTypes.AccountActivated, EventTypes.BalanceUpdated, EventTypes.TransactionCompleted, EventTypes.TransactionFailed, EventTypes.NotificationSent, "transaction.received", "configuration.updated", "reversal.review.completed"];

    public async Task Handle(DomainEvent value, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        if (value.EventType == EventTypes.AssessmentAccountRequested)
        {
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
            if (!AssessmentDataAccess.IsEnabled(configuration, environment))
            {
                throw new InvalidOperationException("Assessment account provisioning is disabled.");
            }

            await using (var connection = await db.OpenServiceDatabaseConnection(ct))
            {
                if (await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                    db.Dialect.FindProcessed, new { Id = value.EventId, Consumer = "identity" },
                    cancellationToken: ct)) is not null)
                {
                    return;
                }
            }

            var data = value.DeserializePayload<AssessmentAccountData>();
            var history = data.History;
            var actor = DisputePortal.Api.Service.Banking.AssessmentAccountHistory.Customer(history.UserId);
            AssessmentDataAccess.AuthorizeImport(new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(actor.Claims.Append(new("caller", "identity")), "assessment")), history);
            await client.SendServiceRequest<AccountHistoryImportResult>(
                "ledger", HttpMethod.Post, "/internal/assessment/accounts", history, actor, ct);
            await client.SendServiceRequest<System.Text.Json.JsonElement>(
                "transactions", HttpMethod.Post, "/internal/assessment/accounts", history, actor, ct);
        }

        var finance = scope.ServiceProvider.GetRequiredService<IFinanceRepository>();
        var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
        await db.ProcessEventWithInboxDeduplication(
            value,
            async (c, tx) =>
        {
            await RelationalDialect.LockOperations(c, tx, db.Dialect.OperationsLockSql, ct);
            await c.ExecuteAsync(new CommandDefinition(db.Dialect.EnableFinancialProjectionSql, transaction: tx, cancellationToken: ct));
            Exception? operationFailure = null;
            try
            {
                if (value.EventType == EventTypes.AssessmentAccountRequested)
                {
                    await ProjectAssessmentHistory(c, tx, value.DeserializePayload<AssessmentAccountData>(), ct);
                }
                else if (value.EventType == "reversal.review.completed")
                {
                    var outcome = value.DeserializePayload<ReversalReviewOutcome>();
                    if (outcome.RequestId == Guid.Empty
                            || outcome.ActorId == Guid.Empty
                            || outcome.OriginalTransactionId == Guid.Empty
                            || outcome.Kind is not ("Refund" or "Recall")
                            || outcome.Status is not ("Completed" or "Failed" or "Ineligible"))
                    {
                        throw new InvalidOperationException("Invalid review outcome.");
                    }

                    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                    var text = scope.ServiceProvider.GetRequiredService<DisputePortal.Api.Interfaces.ExternalServices.IMessageTemplates>().Render(
                            "workflow/RefundReviewOutcome",
                            new Dictionary<string, string>
                            {
                                ["kind"] = outcome.Kind,
                                ["status"] = outcome.Status,
                                ["reason"] = ReviewOutcomeMessages.Describe(configuration, outcome.Status, outcome.Code)
                            });
                    await DisputeNotificationOutbox.EnqueueEvent(
                            c,
                            tx,
                            null,
                            outcome.RequestId,
                            "RefundReview" + outcome.Status,
                            text.Subject,
                            text.Text,
                            [outcome.ActorId],
                            ct,
                            "/admin/dispute-operations?review=" + outcome.RequestId);
                }
                else if (value.EventType == "configuration.updated"
                        && !(value.Payload.TryGetProperty("removed", out var removed)
                        && removed.ValueKind == System.Text.Json.JsonValueKind.True))
                {
                    var settings = await client.SendServiceRequest<PlatformSettings>("configuration", HttpMethod.Get, "/settings", ct: ct);
                    var currency = CountryCurrencyPolicy.Current(settings).DefaultCurrency;
                    await c.ExecuteAsync(new CommandDefinition(
                            SqlScripts.ReadSqlScript("Shared/Queries/Api/IdentityProjectionConsumer/BlockActiveAccountsOutsideProjectedCurrencyPolicy"),
                            new { Currency = currency, settings.Version },
                            tx,
                            cancellationToken: ct));
                }
                else if (value.EventType == "transaction.received")
                {
                    var received = value.DeserializePayload<TransactionRecord>();
                    await ProjectReceived(c, tx, received, protector, ct);
                }
                else if (value.EventType == EventTypes.AccountActivated)
                {
                    var account = value.DeserializePayload<AccountEvent>();
                    await EnsureAccount(c, tx, account.AccountId, protector, ct);
                    var current = await client.SendServiceRequest<AccountRecord>(
                            "accounts",
                            HttpMethod.Get,
                            "/internal/accounts/" + account.AccountId,
                            ct: ct);
                    var settings = await client.SendServiceRequest<PlatformSettings>("configuration", HttpMethod.Get, "/settings", ct: ct);
                    var status = ProjectionStatus(current, settings);
                    await c.ExecuteAsync(new CommandDefinition(
                            """
                            UPDATE "accounts"."accounts"
                            SET
                                "accountStatus" = @Status,
                                "countryPolicyVersion" = @Version
                            WHERE "publicId" = @Id
                            AND "countryPolicyVersion" <= @Version
                            """,
                            new
                            {
                                current.Id,
                                Status = status,
                                Version = Math.Max(current.CountryPolicyVersion, settings.Version)
                            },
                            tx,
                            cancellationToken: ct));
                }
                else if (value.EventType == EventTypes.BalanceUpdated)
                {
                    var balance = value.DeserializePayload<BalanceSnapshot>();
                    // Balance projections refresh the UI; they are not separate user notifications.
                }
                else if (value.EventType is EventTypes.TransactionCompleted or EventTypes.TransactionFailed)
                {
                    var result = value.DeserializePayload<DisputePortal.Platform.Contracts.TransactionEvent>();
                    var isCase = await c.ExecuteScalarAsync<int>(new CommandDefinition(
                            """
                            SELECT
                                COUNT(*)
                            FROM "disputes"."financialCommands" AS "dispute_financial_commands"
                            WHERE "dispute_financial_commands"."publicId" = @Id
                            """,
                            new { Id = result.TransactionId },
                            tx,
                            cancellationToken: ct)) > 0;
                    if (isCase)
                    {
                        await ((ICaseFinancialProjection)finance).ApplyFinancialOutcome(c, tx, value, ct);
                    }
                    else
                    {
                        var completed = value.EventType == EventTypes.TransactionCompleted;
                        if (completed)
                        {
                            await ProjectTransaction(c, tx, result, protector, ct);
                        }

                        if (result.Kind == "Transfer")
                        {
                            var source = await c.QuerySingleAsync<TransferAccount>(new CommandDefinition(
                                    SqlScripts.ReadSqlScript("Shared/Queries/Api/IdentityProjectionConsumer/GetAccountForUser"),
                                    new { Id = result.AccountId, User = result.UserId },
                                    tx,
                                    cancellationToken: ct));
                            TransferAccount? target = null;
                            if (result.RecipientAccountId is { } recipient)
                            {
                                await EnsureAccount(c, tx, recipient, protector, ct);
                                target = await c.QuerySingleOrDefaultAsync<TransferAccount>(new CommandDefinition(
                                        SqlScripts.ReadSqlScript("Shared/Queries/Api/IdentityProjectionConsumer/GetAccountForUser"),
                                        new { Id = recipient, User = result.UserId },
                                        tx,
                                        cancellationToken: ct));
                            }

                            var kind = completed ? "TransferCompleted" : "TransferFailed";
                            var text = scope.ServiceProvider.GetRequiredService<DisputePortal.Api.Interfaces.ExternalServices.IMessageTemplates>().Render(
                                    "push/" + (completed ? "TransferDetails" : "TransferFailureDetails"),
                                    new Dictionary<string, string>
                                    {
                                        ["amount"] = result.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " " + result.Currency,
                                        ["source"] = source.Name + " " + source.Number,
                                        ["target"] = target is null ? "your linked account" : target.Name + " " + target.Number
                                    }).Text;
                            await DisputeNotificationOutbox.EnqueueEvent(
                                    c,
                                    tx,
                                    null,
                                    value.EventId,
                                    kind,
                                    "Dispute Portal",
                                    text,
                                    [result.UserId],
                                    ct,
                                    completed ? "/transactions/" + result.TransactionId : "/accounts/" + result.AccountId);
                        }
                    }
                }
            }
            catch (Exception error)
            {
                operationFailure = error;
                throw;
            }
            finally
            {
                if (operationFailure is null || db.Dialect is not PostgreSqlDialect)
                {
                    try
                    {
                        await c.ExecuteAsync(new CommandDefinition(
                                db.Dialect.DisableFinancialProjectionSql,
                                transaction: tx.Connection is null ? null : tx,
                                cancellationToken: CancellationToken.None));
                    }
                    catch (Exception cleanup)
                    {
                        // Never return a SQL session with a privileged projection
                        // marker to the pool, even when the transaction aborted.
                        if (c is Microsoft.Data.SqlClient.SqlConnection sql)
                        {
                            Microsoft.Data.SqlClient.SqlConnection.ClearPool(sql);
                        }

                        if (operationFailure is null)
                        {
                            throw;
                        }

                        logger?.LogWarning(
                                cleanup,
                                "Projection scope cleanup failed after {ErrorType}; the connection is discarded.",
                                cleanup.GetType().Name);
                    }
                }
            }
        },
            ct);
        var realtime = scope.ServiceProvider.GetService<IRealtimePublisher>();
        if (realtime is not null)
        {
            Guid? owner = value.EventType switch
            {
                EventTypes.AssessmentAccountRequested => value.DeserializePayload<AssessmentAccountData>().History.UserId,
                EventTypes.BalanceUpdated => value.DeserializePayload<BalanceSnapshot>().UserId,
                EventTypes.AccountActivated => value.DeserializePayload<AccountEvent>().UserId,
                EventTypes.TransactionCompleted or EventTypes.TransactionFailed => value.DeserializePayload<DisputePortal.Platform.Contracts.TransactionEvent>().UserId,
                "transaction.received" => value.DeserializePayload<TransactionRecord>().UserId,
                EventTypes.NotificationSent => value.DeserializePayload<NotificationEvent>().UserId,
                "reversal.review.completed" => value.DeserializePayload<ReversalReviewOutcome>().ActorId,
                _ => null
            };
            if (owner is { } id)
            {
                await realtime.PublishPortalUpdate([id], true, ["banking", "disputes", "notifications"], null, ct);
            }
        }
    }


    private static async Task ProjectAssessmentHistory(
        DbConnection connection, DbTransaction transaction, AssessmentAccountData data, CancellationToken ct)
    {
        var history = data.History;
        foreach (var item in history.Transactions)
        {
            data.DebitOrders.TryGetValue(item.Id, out var debitOrder);
            await connection.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/BankingRepository/InsertTransaction"),
                new
                {
                    item.Id,
                    Account = history.AccountId,
                    item.Reference,
                    At = item.OccurredAt,
                    item.Description,
                    item.MerchantName,
                    item.Amount,
                    item.Currency,
                    item.Type,
                    item.Direction,
                    item.Status,
                    Eligible = item.DisputeEligible,
                    DebitOrderScheme = debitOrder?.Scheme,
                    MandateStatus = debitOrder?.MandateStatus
                }, transaction, cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/BankingRepository/InsertBankLinkByAccountAndHashAndId"),
                new { Account = history.AccountId, item.Id, Hash = item.Id.ToString("N") },
                transaction, cancellationToken: ct));
        }

        await ServiceDatabase.InsertServiceAuditEvent(connection, transaction, history.UserId,
            "AssessmentTransactionsImported", history.AccountId,
            "Assessment opening history hash: " + history.Hash, ct);
    }

    private async Task EnsureAccount(DbConnection c, DbTransaction tx, Guid id, ISecretProtector protector, CancellationToken ct)
    {
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT
                COUNT(*)
            FROM "accounts"."accounts" AS "customer_accounts"
            WHERE "customer_accounts"."publicId" = @Id
            """,
            new { Id = id },
            tx,
            cancellationToken: ct)) > 0)
        {
            return;
        }

        var account = await client.SendServiceRequest<AccountRecord>("accounts", HttpMethod.Get, "/internal/accounts/" + id, ct: ct);
        var settings = await client.SendServiceRequest<PlatformSettings>("configuration", HttpMethod.Get, "/settings", ct: ct);
        var status = ProjectionStatus(account, settings);
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/IdentityProjectionConsumer/InsertAccount"),
            new
            {
                account.Id,
                account.UserId,
                account.Type,
                account.Currency,
                Status = status,
                PolicyVersion = Math.Max(account.CountryPolicyVersion, settings.Version),
                Masked = "••••" + account.Number[^4..],
                Protected = protector.Protect(account.Number),
                account.CreatedAt,
                Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            account,
            DomainEvent.Json))),
                Country = account.CountryCode,
                Now = DateTimeOffset.UtcNow
            },
            tx,
            cancellationToken: ct));
    }

    private static string ProjectionStatus(AccountRecord account, PlatformSettings settings)
    {
        if (account.Status == "Closed")
        {
            return "Closed";
        }

        return account.Status == "Active" && CountryCurrencyPolicy.IsCurrent(settings, account.CountryCode, account.Currency) ? "Active" : "Blocked";
    }

    private async Task ProjectTransaction(
        DbConnection c,
        DbTransaction tx,
        DisputePortal.Platform.Contracts.TransactionEvent r,
        ISecretProtector protector,
        CancellationToken ct)
    {
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT
                COUNT(*)
            FROM "transactions"."transactions" AS "transactions"
            WHERE "transactions"."publicId" = @Id
            """,
            new { Id = r.TransactionId },
            tx,
            cancellationToken: ct)) > 0)
        {
            return;
        }

        await EnsureAccount(c, tx, r.AccountId, protector, ct);
        var type = r.Kind == "Withdrawal" ? "CashWithdrawal" : r.Kind == "Recall" ? "RefundReversal" : r.Kind;
        var direction = r.Kind is "Deposit" or "Refund" ? "Credit" : "Debit";
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/IdentityProjectionConsumer/InsertTransactionByIdAndAccountAndReferenceAndNowAndDescription"),
            new
            {
                Id = r.TransactionId,
                Account = r.AccountId,
                r.Reference,
                Now = DateTimeOffset.UtcNow,
                Description = r.Kind,
                r.Amount,
                r.Currency,
                Type = type,
                Direction = direction,
                Eligible = direction == "Debit" && type == "CashWithdrawal"
            },
            tx,
            cancellationToken: ct));
    }

    private async Task ProjectReceived(
        DbConnection c,
        DbTransaction tx,
        TransactionRecord r,
        ISecretProtector protector,
        CancellationToken ct)
    {
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT
                COUNT(*)
            FROM "transactions"."transactions" AS "transactions"
            WHERE "transactions"."publicId" = @Id
            """,
            new { Id = r.Id },
            tx,
            cancellationToken: ct)) > 0)
        {
            return;
        }

        await EnsureAccount(c, tx, r.AccountId, protector, ct);
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/IdentityProjectionConsumer/InsertTransactionByIdAndAccountIdAndReferenceAndAtAndAmount"),
            new
            {
                r.Id,
                r.AccountId,
                r.Reference,
                At = r.CreatedAt,
                r.Amount,
                r.Currency,
                Eligible = false
            },
            tx,
            cancellationToken: ct));
    }

    private class TransferAccount
    {
        public string Name { get; set; } = string.Empty;
        public string Number { get; set; } = string.Empty;

        public TransferAccount()
        {
        }

        public TransferAccount(string Name, string Number)
        {
            this.Name = Name;
            this.Number = Number;
        }
    }
}
