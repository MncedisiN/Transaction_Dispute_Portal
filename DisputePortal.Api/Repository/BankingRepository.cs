using DisputePortal.Api.Models.Banking;
using System.Data.Common;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using Dapper;
using System.Security.Cryptography;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Responses;
using System.Text;
using DisputePortal.Api.Service.Banking;
using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Banking;
using DisputePortal.Api.Interfaces.Security;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;

public class BankingRepository(
    IDatabaseConnectionFactory factory,
    IDatabaseDialect databaseDialect,
    TimeZoneInfo businessTimeZone,
    ISecretProtector secretProtector,
    IOtpHasher otpHasher,
    IPortalSettingsRepository portalSettings,
    ICustomerTransactionPresentation? presentation = null,
    IServiceClient? platform = null,
    ILogger<BankingRepository>? logger = null) : RepositoryBase(factory, databaseDialect), ITransactionRepository, IBankFeedStore
{
    private static readonly string FeedSelect = SqlScripts.ReadSqlScript("Shared/Queries/Api/BankingRepository/GetBankLinksForCustomerAndAccount");

    private static string SourceHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private BankFeedBinding FeedBinding(FeedRow row) => new(
        row.AccountId,
        row.Provider,
        secretProtector.Unprotect(row.ProtectedAccount),
        row.ProtectedCursor is null ? null : secretProtector.Unprotect(row.ProtectedCursor),
        row.Revision,
        row.LastSyncedAt);

    public async Task<BankFeedBinding?> GetOwnedBinding(Guid customerId, Guid accountId, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        var args = new
        {
            Account = accountId,
            Customer = customerId
        };
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/BankingRepository/CountAccountsForCustomerAndAccount"),
            args,
            cancellationToken: ct)) != 1)
        {
            throw new PortalNotFoundException();
        }

        var row = await c.QuerySingleOrDefaultAsync<FeedRow>(new CommandDefinition(FeedSelect, args, cancellationToken: ct));
        return row is null ? null : FeedBinding(row);
    }

    public async Task LinkOwnedAccount(Guid customerId, Guid accountId, string provider, string externalAccountId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(provider)
            || provider.Length > 60
            || string.IsNullOrWhiteSpace(externalAccountId)
            || externalAccountId.Length > 200)
        {
            throw new PortalValidationException("A valid provider and account reference are required.");
        }

        await using var connection = await connections.OpenConnection(ct);
        var currency = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/BankingRepository/GetActiveAccountForCustomerAndAccount"),
            new { Account = accountId, Customer = customerId },
            cancellationToken: ct));
        if (currency is null)
        {
            throw new PortalNotFoundException();
        }

        var client = platform ?? throw new PlatformException(
            503,
            "country_policy_unavailable",
            "The saved country policy is temporarily unavailable. Try again shortly.");
        var settings = await client.SendServiceRequest<PlatformSettings>("configuration", HttpMethod.Get, "/settings", ct: ct);
        var country = CountryCurrencyPolicy.Current(settings);
        if (currency != country.DefaultCurrency)
        {
            throw new PortalConflictException("Only accounts in the saved country's default currency can be connected. Existing money and history are unchanged.");
        }

        // A claim identifies one connection attempt. The persisted link remains the idempotency source.
        var hash = SourceHash(externalAccountId);
        var request = Guid.NewGuid();
        DbTransaction? transaction = null;
        var committing = false;
        try
        {
            var claim = await client.SendServiceRequest<CountryOperation>(
                "configuration",
                HttpMethod.Post,
                "/country-operations",
                new CountryOperationRequest
                {
                    RequestId = request,
                    SourceKind = "Connection",
                    CountryCode = country.Code,
                    Currency = currency,
                    ExpectedVersion = settings.Version
                },
                ct: ct);
            transaction = await connection.BeginTransactionAsync(ct);
            await LockOperations(connection, transaction, ct);
            if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/BankingRepository/CountActiveAccountsForCustomerAndAccount"),
                new
                {
                    Account = accountId,
                    Customer = customerId,
                    Currency = currency
                },
                transaction,
                cancellationToken: ct)) != 1)
            {
                throw new PortalConflictException("The account changed. Refresh its details before connecting it.");
            }

            var existing = await connection.QuerySingleOrDefaultAsync<FeedRow>(new CommandDefinition(
                FeedSelect,
                new { Account = accountId, Customer = customerId },
                transaction,
                cancellationToken: ct));
            if (existing is not null)
            {
                var binding = FeedBinding(existing);
                if (binding.Provider != provider || binding.ExternalAccountId != externalAccountId)
                {
                    throw new PortalConflictException("This account already has a different banking connection.");
                }
            }
            else
            {
                if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    """
                    SELECT
                        COUNT(*)
                    FROM "accounts"."bankLinks"
                    WHERE provider = @Provider
                    AND "externalAccountHash" = @Hash
                    """,
                    new { Provider = provider, Hash = hash },
                    transaction,
                    cancellationToken: ct)) > 0)
                {
                    throw new PortalConflictException("This banking account is already linked.");
                }

                await connection.ExecuteAsync(new CommandDefinition(
                    SqlScripts.ReadSqlScript("Shared/Queries/Api/BankingRepository/InsertBankLinkByProviderAndHashAndProtectedAndAccount"),
                    new
                    {
                        Account = accountId,
                        Provider = provider,
                        Hash = hash,
                        Protected = secretProtector.Protect(externalAccountId)
                    },
                    transaction,
                    cancellationToken: ct));
                await AuditOperation(connection, transaction, customerId, "BankAccountLinked", accountId, new { Provider = provider }, ct);
            }

            // Settling is durable with the link commit, including process failure
            // between commit and response. It produces no customer notification.
            await ServiceDatabase.SaveDomainEventToOutbox(
                connection,
                transaction,
                DomainEvent.CreateDomainEvent(
                    "identity",
                    "country.operation.settled",
                    new CountryOperationSettlement
                    {
                        RequestId = claim.RequestId,
                        OperationId = claim.OperationId,
                        SourceKind = claim.SourceKind
                    }),
                ct);
            committing = true;
            await transaction.CommitAsync(ct);
        }
        catch
        {
            var rolledBack = transaction is null;
            if (transaction?.Connection is not null)
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    rolledBack = true;
                }
                catch (Exception rollbackFailure)
                {
                    logger?.LogWarning(
                        "Country connection rollback awaits reconciliation {RequestId} {FailureType}",
                        request,
                        rollbackFailure.GetType().Name);
                }
            }

            // An uncertain commit is reconciled by its durable event/retry;
            // never release the fence on an assumption that it did not commit.
            if (!committing && rolledBack)
            {
                try
                {
                    await client.SendServiceRequest<object>(
                        "configuration",
                        HttpMethod.Post,
                        "/country-operations/Connection/" + request + "/settle",
                        ct: CancellationToken.None);
                }
                catch (Exception failure) when (failure is HttpRequestException or OperationCanceledException or PlatformException)
                {
                    logger?.LogWarning(
                        "Country connection claim awaits reconciliation {RequestId} {FailureType}",
                        request,
                        failure.GetType().Name);
                }
            }

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    public async Task<int> ApplyPage(Guid customerId, BankFeedBinding expected, BankTransactionPage page, CancellationToken ct)
    {
        BankFeedService.ValidatePage(expected, page);
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        var row = await c.QuerySingleOrDefaultAsync<FeedRow>(new CommandDefinition(
            FeedSelect,
            new { Account = expected.AccountId, Customer = customerId },
            tx,
            cancellationToken: ct)) ?? throw new PortalNotFoundException();
        var actual = FeedBinding(row);
        if (actual.Revision != expected.Revision
            || actual.Provider != expected.Provider
            || actual.ExternalAccountId != expected.ExternalAccountId
            || actual.Cursor != expected.Cursor)
        {
            throw new PortalConflictException("The account feed advanced. Refresh and try again.");
        }

        var currency = await c.ExecuteScalarAsync<string>(new CommandDefinition(
            """
            SELECT
                "customer_accounts"."currencyCode" AS "currency"
            FROM "accounts"."accounts" AS "customer_accounts"
            WHERE "customer_accounts"."publicId" = @Id
            AND "customer_accounts"."accountStatus" = 'Active'
            """,
            new { Id = actual.AccountId },
            tx,
            cancellationToken: ct));
        if (currency is null)
        {
            throw new PortalConflictException("This account is not active.");
        }

        foreach (var item in page.Items)
        {
            if (item.Currency != currency)
            {
                throw new PortalValidationException("Bank transaction amounts must use the account's booked currency.");
            }

            // PostgreSQL stores microseconds; normalize before comparing immutable snapshots.
            var occurredAt = new DateTimeOffset(item.OccurredAt.UtcTicks - item.OccurredAt.UtcTicks % 10, TimeSpan.Zero);
            var hash = SourceHash(item.ExternalId);
            var existing = await c.QuerySingleOrDefaultAsync<ImportedRow>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/BankingRepository/GetBankLinkForAccount"),
                new { Account = actual.AccountId, Hash = hash },
                tx,
                cancellationToken: ct));
            if (existing is not null
                && (existing.Amount != item.Amount
                || existing.Currency != item.Currency
                || existing.TransactionAt != occurredAt
                || existing.Description != item.Description
                || existing.MerchantName != item.MerchantName
                || existing.Type != item.Type
                || existing.Direction != item.Direction))
            {
                throw new PortalConflictException("The feed changed an original transaction. Reconciliation is required; the original record was preserved.");
            }

            var id = existing?.Id ?? Guid.NewGuid();
            var args = new
            {
                Id = id,
                Account = actual.AccountId,
                Reference = "BANK-" + id.ToString("N"),
                At = occurredAt,
                item.Description,
                item.MerchantName,
                item.Amount,
                item.Currency,
                item.Type,
                item.Direction,
                item.Status,
                Eligible = item.DisputeEligible && item.Direction == "Debit" && item.Status == "Posted" && item.Amount > 0,
                item.DebitOrderScheme,
                item.MandateStatus
            };
            if (existing is null)
            {
                await c.ExecuteAsync(new CommandDefinition(
                    SqlScripts.ReadSqlScript("Shared/Queries/Api/BankingRepository/InsertTransaction"),
                    args,
                    tx,
                    cancellationToken: ct));
                await c.ExecuteAsync(new CommandDefinition(
                    SqlScripts.ReadSqlScript("Shared/Queries/Api/BankingRepository/InsertBankLinkByAccountAndHashAndId"),
                    new
                    {
                        Account = actual.AccountId,
                        Hash = hash,
                        Id = id
                    },
                    tx,
                    cancellationToken: ct));
            }
            else
            {
                await c.ExecuteAsync(new CommandDefinition(
                    SqlScripts.ReadSqlScript("Shared/Queries/Api/BankingRepository/UpdateTransactionsForAccount"),
                    args,
                    tx,
                    cancellationToken: ct));
            }
        }

        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/BankingRepository/UpdateBankLinksForAccount"),
            new
            {
                Account = actual.AccountId,
                Cursor = page.NextCursor is null ? null : secretProtector.Protect(page.NextCursor),
                Now = DateTimeOffset.UtcNow
            },
            tx,
            cancellationToken: ct));
        await AuditOperation(
            c,
            tx,
            customerId,
            "BankTransactionsSynced",
            actual.AccountId,
            new
            {
                actual.Provider,
                Count = page.Items.Count,
                page.HasMore
            },
            ct);
        await tx.CommitAsync(ct);
        return page.Items.Count;
    }

    private string TransactionSelect => SqlScripts.RenderSqlScript(
        "Shared/Queries/Api/BankingRepository/GetTransactions",
        ("EligibleTransactionExpression", dialect.EligibleTransactionExpression),
        ("ExistsAsBoolean", dialect.ExistsAsBoolean("""
        SELECT
            1
        FROM "disputes"."disputes" d
        WHERE d."transactionId" = t."id"
        AND d."status" IN ('Submitted', 'UnderReview', 'AwaitingCustomer')
        """)));
    private OwnedAccountNumberReader AccountNumbers => new(
        dialect,
        secretProtector,
        otpHasher,
        LockOperations,
        (connection, transaction, account, ct) => AuditOperation(
            connection,
            transaction,
            null,
            "LocalAccountNumberAssigned",
            account,
            new { AccountId = account, Source = "LocalAllocation" },
            ct));

    public async Task<IReadOnlyList<AccountDto>> GetAccounts(Guid customerId, CancellationToken cancellationToken)
    {
        var sql = SqlScripts.ReadSqlScript("Shared/Queries/Api/BankingRepository/GetActiveAccountsForCustomer");
        await using var connection = await connections.OpenConnection(cancellationToken);
        var rows = (await connection.QueryAsync<AccountRow>(new CommandDefinition(
            sql,
            new { CustomerId = customerId },
            cancellationToken: cancellationToken))).ToArray();
        var numbers = await AccountNumbers.GetOwnedAccountNumbers(
            connection,
            null,
            customerId,
            rows.Select(row => row.Id),
            cancellationToken);
        return rows.Select(row => new AccountDto(
            row.Id,
            row.AccountType,
            row.Currency,
            row.MaskedAccountNumber,
            row.Status,
            row.RegisteredAt,
            numbers.GetValueOrDefault(row.Id))).ToArray();
    }

    public async Task<AccountDto?> GetAccountByPublicId(Guid customerPublicId, Guid accountPublicId, CancellationToken cancellationToken)
    {
        var sql = SqlScripts.ReadSqlScript("Shared/Queries/Api/BankingRepository/GetActiveAccounts");
        await using var connection = await connections.OpenConnection(cancellationToken);
        var account = await connection.QuerySingleOrDefaultAsync<AccountRow>(new CommandDefinition(
            sql,
            new { CustomerPublicId = customerPublicId, AccountPublicId = accountPublicId },
            cancellationToken: cancellationToken));
        if (account is null)
        {
            return null;
        }

        var numbers = await AccountNumbers.GetOwnedAccountNumbers(connection, null, customerPublicId, [account.Id], cancellationToken);
        return new AccountDto(
            account.Id,
            account.AccountType,
            account.Currency,
            account.MaskedAccountNumber,
            account.Status,
            account.RegisteredAt,
            numbers.GetValueOrDefault(account.Id));
    }

    public async Task<PageResult<TransactionDto>> GetTransactions(
        Guid customerId,
        TransactionFilter filter,
        CancellationToken cancellationToken)
    {
        var settings = await portalSettings.GetPortalSettings(cancellationToken);
        var historyDays = settings.Settings.ClientOptions["transactionHistoryDays"];
        var where = SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/BankingRepository/FilterCustomerTransactionsByAccountSearchAndDate",
            ("AccountOpeningWindowEnd", dialect.AddDaysToDateExpression("a.\"createdAt\"", "@TransactionHistoryDays")),
            ("SearchPredicate", dialect.LikeExpression("t.description", "@SearchPattern")),
            ("RelatedSearchPredicate", dialect.LikeExpression("t.\"merchantName\"", "@SearchPattern")),
            ("AdditionalSearchPredicate", dialect.LikeExpression("t.\"transactionReference\"", "@SearchPattern")));
        var search = CleanSearch(filter.Search)?.ToLowerInvariant();
        var dates = new HistoryFilter
        {
            Status = null,
            From = filter.From,
            To = filter.To
        };
        dates.ValidateHistoryFilter();
        DateTimeOffset? fromDate = dates.StartUtc(businessTimeZone), toDateExclusive = dates.EndUtc(businessTimeZone);
        var args = new
        {
            filter.AccountId,
            TransactionHistoryDays = historyDays,
            CustomerId = customerId,
            SearchPattern = search,
            FromDate = fromDate,
            ToDateExclusive = toDateExclusive,
            filter.Direction,
            filter.Type,
            Offset = PageOffset(filter.Page, filter.PageSize),
            filter.PageSize
        };
        var orderBy = filter.Sort switch
        {
            "date_asc" => "t.\"createdAt\" ASC, t.id ASC",
            "amount_desc" => "t.amount DESC, t.\"createdAt\" DESC, t.id DESC",
            "amount_asc" => "t.amount ASC, t.\"createdAt\" DESC, t.id DESC",
            _ => "t.\"createdAt\" DESC, t.id DESC"
        };
        var sql = SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/BankingRepository/GetBankingPage",
            ("TransactionSelect", TransactionSelect),
            ("FilterClause", where),
            ("OrderBy", orderBy),
            ("PaginationClause", dialect.PaginationClause),
            ("RelatedFilterClause", where));
        await using var connection = await connections.OpenConnection(cancellationToken);
        TransactionRow[] rows;
        long total;
        using (var results = await connection.QueryMultipleAsync(new CommandDefinition(sql, args, cancellationToken: cancellationToken)))
        {
            rows = (await results.ReadAsync<TransactionRow>()).ToArray();
            total = await results.ReadSingleAsync<long>();
        }

        var numbers = await AccountNumbers.GetOwnedAccountNumbers(
            connection,
            null,
            customerId,
            rows.Select(row => row.AccountId),
            cancellationToken);
        var display = await (presentation ?? CustomerTransactionPresentation.Default).LoadTransactionPresentationRules(cancellationToken);
        var items = rows.Select(row => MapTransactionSummaries(row, numbers.GetValueOrDefault(row.AccountId), display)).ToArray();
        return new PageResult<TransactionDto>(items, filter.Page, filter.PageSize, total);
    }

    public async Task<TransactionDto?> GetTransaction(Guid customerId, Guid transactionId, CancellationToken cancellationToken)
    {
        var sql = SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/BankingRepository/GetOwnedOriginalTransactionDetails",
            ("TransactionSelect", TransactionSelect));
        await using var connection = await connections.OpenConnection(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<TransactionRow>(new CommandDefinition(
            sql,
            new { CustomerId = customerId, TransactionId = transactionId },
            cancellationToken: cancellationToken));
        if (row is null)
        {
            return null;
        }

        var numbers = await AccountNumbers.GetOwnedAccountNumbers(connection, null, customerId, [row.AccountId], cancellationToken);
        var display = await (presentation ?? CustomerTransactionPresentation.Default).LoadTransactionPresentationRules(cancellationToken);
        return MapTransactionSummaries(row, numbers.GetValueOrDefault(row.AccountId), display);
    }

    private TransactionDto MapTransactionSummaries(TransactionRow x, string? accountNumber, ICustomerTransactionPresentation display) =>
        new(
        x.Id,
        x.AccountId,
        accountNumber ?? string.Empty,
        x.Type == "Refund" ? x.Reference : null,
        x.TransactionAt,
        display.Description(x.Type, x.MerchantName, x.Description),
        x.MerchantName,
        x.Amount,
        x.Currency,
        x.Type,
        x.Direction,
        x.Status,
        x.DisputeEligible,
        x.HasActiveDispute,
        x.DisputeId,
        x.DisputeStatus,
        display.TypeLabel(x.Type, x.TypeLabel),
        x.AccountType);

    public async Task<IReadOnlyList<TransactionTypeDto>> GetTransactionTypes(CancellationToken ct)
    {
        var display = await (presentation ?? CustomerTransactionPresentation.Default).LoadTransactionPresentationRules(ct);
        await using var connection = await connections.OpenConnection(ct);
        return (await connection.QueryAsync<TransactionTypeDto>(new CommandDefinition(
            """
            SELECT
                "transaction_types"."code" AS TypeKey,
                "transaction_types"."displayName" AS Label
            FROM "configuration"."transactionTypes" AS "transaction_types"
            ORDER BY "transaction_types"."sortOrder", "transaction_types"."code"
            """,
            cancellationToken: ct))).Select(t => new TransactionTypeDto(t.TypeKey, display.TypeLabel(t.TypeKey, t.Label))).ToArray();
    }

    private class FeedRow
    {
        public Guid AccountId { get; set; }
        public string Provider { get; set; } = "";
        public string ProtectedAccount { get; set; } = "";
        public string? ProtectedCursor { get; set; }
        public int Revision { get; set; }
        public DateTimeOffset? LastSyncedAt { get; set; }
    }

    private class ImportedRow
    {
        public Guid Id { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "";
        public DateTimeOffset TransactionAt { get; set; }
        public string Description { get; set; } = "";
        public string MerchantName { get; set; } = "";
        public string Type { get; set; } = "";
        public string Direction { get; set; } = "";
    }

    private class TransactionRow
    {
        public Guid Id { get; set; }
        public Guid AccountId { get; set; }
        public string AccountType { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
        public DateTimeOffset TransactionAt { get; set; }
        public string Description { get; set; } = string.Empty;
        public string MerchantName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Direction { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool DisputeEligible { get; set; }
        public bool HasActiveDispute { get; set; }
        public Guid? DisputeId { get; set; }
        public string? DisputeStatus { get; set; }
        public string TypeLabel { get; set; } = string.Empty;

        public TransactionRow()
        {
        }

        public TransactionRow(
            Guid Id,
            Guid AccountId,
            string AccountType,
            string Reference,
            DateTimeOffset TransactionAt,
            string Description,
            string MerchantName,
            decimal Amount,
            string Currency,
            string Type,
            string Direction,
            string Status,
            bool DisputeEligible,
            bool HasActiveDispute,
            Guid? DisputeId,
            string? DisputeStatus,
            string TypeLabel)
        {
            this.Id = Id;
            this.AccountId = AccountId;
            this.AccountType = AccountType;
            this.Reference = Reference;
            this.TransactionAt = TransactionAt;
            this.Description = Description;
            this.MerchantName = MerchantName;
            this.Amount = Amount;
            this.Currency = Currency;
            this.Type = Type;
            this.Direction = Direction;
            this.Status = Status;
            this.DisputeEligible = DisputeEligible;
            this.HasActiveDispute = HasActiveDispute;
            this.DisputeId = DisputeId;
            this.DisputeStatus = DisputeStatus;
            this.TypeLabel = TypeLabel;
        }
    }

    private class AccountRow
    {
        public Guid Id { get; set; }
        public string AccountType { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public string MaskedAccountNumber { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTimeOffset RegisteredAt { get; set; }

        public AccountRow()
        {
        }

        public AccountRow(
            Guid Id,
            string AccountType,
            string Currency,
            string MaskedAccountNumber,
            string Status,
            DateTimeOffset RegisteredAt)
        {
            this.Id = Id;
            this.AccountType = AccountType;
            this.Currency = Currency;
            this.MaskedAccountNumber = MaskedAccountNumber;
            this.Status = Status;
            this.RegisteredAt = RegisteredAt;
        }
    }
}
