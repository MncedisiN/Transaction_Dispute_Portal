using System.Data.Common;
using System.Security.Cryptography;
using Dapper;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Accounts.Interfaces;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.Accounts;

public class AccountStore(ServiceDatabase db, IServiceClient client) : IAccountQueries, IAccountCommands
{
    public static readonly string Schema = SqlScripts.ReadSqlScript("PostgreSql/Schema/Accounts/AccountStore/CreateAccountSchema");
    private static readonly string Select = SqlScripts.ReadSqlScript("PostgreSql/Queries/Accounts/AccountStore/SelectAccountDetails");

    private Task<PlatformSettings> Settings(CancellationToken ct) => client.SendServiceRequest<PlatformSettings>(
        "configuration",
        HttpMethod.Get,
        "/settings",
        ct: ct);

    private static AccountRecord Effective(AccountRecord account, PlatformSettings settings) =>
        account.Status == "Active" && !CountryCurrencyPolicy.IsCurrent(settings, account.CountryCode, account.Currency) ? new global::DisputePortal.Platform.Contracts.AccountRecord(account)
        {
            Status = "Blocked"
        }

    : account;

    public async Task<AccountRecord> GetAccountById(Guid id, CancellationToken ct)
    {
        var settings = await Settings(ct);
        await using var connection = await db.OpenServiceDatabaseConnection(ct);
        var account = await connection.QuerySingleOrDefaultAsync<AccountRecord>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Accounts/AccountStore/GetAccountByPublicId"),
            new
            {
                Id = id
            },
            cancellationToken: ct)) ?? throw new PlatformException(404, "account_missing", "Account not found.");
        return Effective(account, settings);
    }

    public async Task<AccountRecord[]> GetCustomerAccounts(Guid user, CancellationToken ct)
    {
        var settings = await Settings(ct);
        await using var connection = await db.OpenServiceDatabaseConnection(ct);
        var rows = await connection.QueryAsync<AccountRecord>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Accounts/AccountStore/GetCustomerAccountsOrderedByCreation"),
            new
            {
                User = user
            },
            cancellationToken: ct));
        return rows.Select(account => Effective(account, settings)).ToArray();
    }

    public async Task<object> FindAccountByNumber(string number, CancellationToken ct)
    {
        if (number.Length is < 8 or > 24 || number.Any(character => !char.IsAsciiDigit(character)))
        {
            throw new PlatformException(400, "invalid_number", "Enter a valid account number.");
        }

        var settings = await Settings(ct);
        var country = CountryCurrencyPolicy.Current(settings);
        await using var connection = await db.OpenServiceDatabaseConnection(ct);
        var account = await connection.QuerySingleOrDefaultAsync<AccountRecord>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Accounts/AccountStore/GetActiveAccountByNumberAndCountryAndCurrency"),
            new
            {
                Number = number,
                Country = country.Code,
                Currency = country.DefaultCurrency
            },
            cancellationToken: ct)) ?? throw new PlatformException(404, "account_missing", "Account not found.");
        return new
        {
            account.Id,
            account.Number,
            account.Currency,
            account.Type,
            account.CountryCode
        };
    }

    public Task<AccountRecord> OpenCustomerAccount(Guid user, OpenAccountRequest request, CancellationToken ct) => OpenCustomerAccount(
        user,
        request,
        null,
        ct);

    private async Task<AccountRecord> OpenCustomerAccount(Guid user, OpenAccountRequest request, DomainEvent? cause, CancellationToken ct)
    {
        if (user == Guid.Empty || request.RequestId == Guid.Empty)
        {
            throw new PlatformException(400, "request_required", "Use a request identifier.");
        }

        // A completed idempotent request is readable after a country change. It never creates another account.
        await using (var connection = await db.OpenServiceDatabaseConnection(ct))
        {
            var prior = await connection.QuerySingleOrDefaultAsync<AccountRecord>(new CommandDefinition(
                SqlScripts.ReadSqlScript("PostgreSql/Queries/Accounts/AccountStore/GetAccountByOpeningRequestId"),
                new
                {
                    Request = request.RequestId
                },
                cancellationToken: ct));
            if (prior is not null)
            {
                RequireReplay(prior, user, request);
                return Effective(prior, await Settings(ct));
            }
        }

        var settings = await Settings(ct);
        var country = CountryCurrencyPolicy.Current(settings);
        if ((!string.IsNullOrEmpty(request.CountryCode)
            && request.CountryCode != country.Code)
            || (!string.IsNullOrEmpty(request.Currency)
            && request.Currency != country.DefaultCurrency))
        {
            throw new PlatformException(
                409,
                "currency_unavailable",
                "Only the saved country's default currency is available for new accounts.");
        }

        if (!(settings.AccountTypes ?? []).Contains(request.Type))
        {
            throw new PlatformException(400, "account_unavailable", "Select a configured account type.");
        }

        var normalized = new OpenAccountRequest(request.RequestId, request.Type, country.Code, country.DefaultCurrency);
        CountryOperation? operation = null;
        try
        {
            operation = await client.SendServiceRequest<CountryOperation>(
                "configuration",
                HttpMethod.Post,
                "/country-operations",
                new CountryOperationRequest
                {
                    RequestId = request.RequestId,
                    SourceKind = "Account",
                    CountryCode = country.Code,
                    Currency = country.DefaultCurrency,
                    CustomerId = user,
                    ExpectedVersion = settings.Version
                },
                ct: ct);
            await using var connection = await db.OpenServiceDatabaseConnection(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            var result = await InsertPendingLedgerAccount(connection, transaction, user, normalized, settings, operation, cause, ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch
        {
            // Only a confirmed absent local write under the same owner lock may release a claim.
            // An uncertain commit or failed cancellation check leaves the durable fence for recovery.
            using var recovery = new CancellationTokenSource(db.CleanupTimeout);
            try
            {
                await CancelIfAbsent(user, request.RequestId, operation?.OperationId, recovery.Token);
            }
            catch
            {
            }

            throw;
        }
    }

    private static void RequireReplay(AccountRecord prior, Guid user, OpenAccountRequest request)
    {
        if (prior.UserId != user
            || prior.Type != request.Type
            || !string.IsNullOrEmpty(request.CountryCode)
            && prior.CountryCode != request.CountryCode
            || !string.IsNullOrEmpty(request.Currency)
            && prior.Currency != request.Currency)
        {
            throw new PlatformException(409, "request_conflict", "This request identifier was used for different account details.");
        }
    }

    private async Task CancelIfAbsent(Guid user, Guid requestId, Guid? operationId, CancellationToken ct)
    {
        await using var connection = await db.OpenServiceDatabaseConnection(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await OwnerLock(connection, transaction, user, ct);
        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT
                COUNT(*)
            FROM accounts."accounts"
            WHERE "requestId" = @RequestId
            OR "publicId" = @OperationId
            """,
            new
            {
                RequestId = requestId,
                OperationId = operationId
            },
            transaction,
            cancellationToken: ct)) == 0)
        {
            await client.SendServiceRequest<object>(
                "configuration",
                HttpMethod.Post,
                $"/country-operations/Account/{requestId}/settle?customerId={user}",
                ct: ct);
        }

        await transaction.CommitAsync(ct);
    }

    private static Task OwnerLock(DbConnection connection, DbTransaction transaction, Guid user, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition(
        """
        SELECT
            pg_advisory_xact_lock(hashtext(@Owner))
        """,
        new
        {
            Owner = user.ToString()
        },
        transaction,
        cancellationToken: ct));

    private async Task<AccountRecord> InsertPendingLedgerAccount(
        DbConnection connection,
        DbTransaction transaction,
        Guid user,
        OpenAccountRequest request,
        PlatformSettings settings,
        CountryOperation operation,
        DomainEvent? cause,
        CancellationToken ct)
    {
        await OwnerLock(connection, transaction, user, ct);
        var prior = await connection.QuerySingleOrDefaultAsync<AccountRecord>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Accounts/AccountStore/GetAccountByOpeningRequestId"),
            new
            {
                Request = request.RequestId
            },
            transaction,
            cancellationToken: ct));
        if (prior is not null)
        {
            RequireReplay(prior, user, request);
            return prior;
        }

        var current = await client.SendServiceRequest<CountryOperation>(
            "configuration",
            HttpMethod.Get,
            $"/country-operations/operations/{operation.OperationId}",
            ct: ct);
        if (current.CustomerId != user
            || current.RequestId != request.RequestId
            || current.SourceKind != "Account"
            || current.SettledAt is not null
            || current.CountryCode != request.CountryCode
            || current.Currency != request.Currency)
        {
            throw new PlatformException(409, "operation_settled", "Review this account request again.");
        }

        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT
                COUNT(*)
            FROM accounts."accounts"
            WHERE "customerPublicId" = @User
            AND status <> 'Closed'
            """,
            new
            {
                User = user
            },
            transaction,
            cancellationToken: ct)) >= settings.MaximumAccounts)
        {
            throw new PlatformException(409, "account_limit", "Your configured account limit has been reached.");
        }

        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT
                COUNT(*)
            FROM accounts."accounts"
            WHERE "customerPublicId" = @User
            AND "accountType" = @Type
            AND "countryCode" = @CountryCode
            AND currency = @Currency
            AND status <> 'Closed'
            """,
            new
            {
                User = user,
                request.Type,
                request.CountryCode,
                request.Currency
            },
            transaction,
            cancellationToken: ct)) != 0)
        {
            throw new PlatformException(409, "account_exists", "An account of this type already exists for this country.");
        }

        var number = string.Concat(Enumerable.Range(0, settings.AccountNumberDigits).Select(_ => RandomNumberGenerator.GetInt32(0, 10).ToString()));
        if (number[0] == '0')
        {
            number = "1" + number[1..];
        }

        await connection.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Accounts/AccountStore/InsertPendingLedgerAccount"),
            new
            {
                Id = operation.OperationId,
                User = user,
                Request = request.RequestId,
                Number = number,
                request.Type,
                request.CountryCode,
                request.Currency,
                PolicyVersion = operation.PolicyVersion
            },
            transaction,
            cancellationToken: ct));
        await ServiceDatabase.SaveDomainEventToOutbox(
            connection,
            transaction,
            DomainEvent.CreateDomainEvent(
                "accounts",
                EventTypes.AccountCreated,
                new AccountEvent
                {
                    AccountId = operation.OperationId,
                    UserId = user,
                    CountryCode = request.CountryCode,
                    Currency = request.Currency,
                    AccountType = request.Type
                },
                cause?.CorrelationId,
                cause?.EventId),
            ct);
        await ServiceDatabase.InsertServiceAuditEvent(
            connection,
            transaction,
            user,
            "AccountCreated",
            operation.OperationId,
            "Account awaiting ledger activation.",
            ct);
        return await connection.QuerySingleAsync<AccountRecord>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Accounts/AccountStore/GetAccountByPublicId"),
            new
            {
                Id = operation.OperationId
            },
            transaction,
            cancellationToken: ct));
    }

    public async Task ApplyCountryPolicy(PlatformSettings settings, CancellationToken ct)
    {
        var country = CountryCurrencyPolicy.Current(settings);
        await using var connection = await db.OpenServiceDatabaseConnection(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Accounts/AccountStore/BlockAccountsOutsideSavedCountryPolicy"),
            new
            {
                Country = country.Code,
                Currency = country.DefaultCurrency,
                PolicyVersion = settings.Version
            },
            cancellationToken: ct));
    }

    public async Task<CountryChangeEligibility> CountryEligibility(CancellationToken ct)
    {
        await using var connection = await db.OpenServiceDatabaseConnection(ct);
        return await connection.QuerySingleAsync<CountryChangeEligibility>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Accounts/AccountStore/GetAccountCountryChangeBlockers"),
            cancellationToken: ct));
    }

    public async Task Handle(DomainEvent value, CancellationToken ct)
    {
        if (value.EventType == EventTypes.UserRegistered)
        {
            await using (var connection = await db.OpenServiceDatabaseConnection(ct))
            {
                if (await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                    db.Dialect.FindProcessed,
                    new { Id = value.EventId, Consumer = "accounts" },
                    cancellationToken: ct)) is not null)
                {
                    return;
                }
            }

            var registered = value.DeserializePayload<UserRegisteredEvent>();
            var settings = await Settings(ct);
            // Registration country describes residence. Account jurisdiction always comes from saved platform policy.
            await OpenCustomerAccount(
                registered.UserId,
                new OpenAccountRequest(value.EventId, (settings.AccountTypes ?? []).First(), "", ""),
                value,
                ct);
            await db.ProcessEventWithInboxDeduplication(value, (_, _) => Task.CompletedTask, ct);
            return;
        }

        if (value.EventType == "configuration.updated")
        {
            if (!(value.Payload.TryGetProperty("removed", out var removed) && removed.ValueKind == System.Text.Json.JsonValueKind.True))
            {
                await ApplyCountryPolicy(await Settings(ct), ct);
            }

            await db.ProcessEventWithInboxDeduplication(value, (_, _) => Task.CompletedTask, ct);
            return;
        }

        if (value.EventType == "ledger.account.created")
        {
            var account = value.DeserializePayload<AccountEvent>();
            var settings = await Settings(ct);
            var eligible = CountryCurrencyPolicy.IsCurrent(settings, account.CountryCode, account.Currency);
            CountryOperation? operation = null;
            if (eligible)
            {
                operation = await client.SendServiceRequest<CountryOperation>(
                    "configuration",
                    HttpMethod.Get,
                    $"/country-operations/operations/{account.AccountId}",
                    ct: ct);
            }

            await db.ProcessEventWithInboxDeduplication(
                value,
                async (connection, transaction) =>
            {
                if (!eligible
                        || operation is null
                        || operation.SettledAt is not null
                        || operation.SourceKind is not ("Account" or "Connection")
                        || operation.CountryCode != account.CountryCode
                        || operation.Currency != account.Currency)
                {
                    return;
                }

                if (await connection.ExecuteAsync(new CommandDefinition(
                        SqlScripts.ReadSqlScript("PostgreSql/Queries/Accounts/AccountStore/ActivateAccountAfterLedgerCreation"),
                        new
                        {
                            account.AccountId,
                            account.UserId,
                            account.CountryCode,
                            account.Currency,
                            PolicyVersion = operation.PolicyVersion
                        },
                        transaction,
                        cancellationToken: ct)) == 1)
                {
                    await ServiceDatabase.SaveDomainEventToOutbox(
                            connection,
                            transaction,
                            DomainEvent.CreateDomainEvent(
                                "accounts",
                                EventTypes.AccountActivated,
                                account,
                                value.CorrelationId,
                                value.EventId),
                            ct);
                }
            },
                ct);
            return;
        }

        await db.ProcessEventWithInboxDeduplication(
            value,
            async (connection, transaction) =>
        {
            var balances = value.EventType == EventTypes.LedgerTransactionPosted ? value.DeserializePayload<LedgerPostedEvent>().Balances : [value.DeserializePayload<BalanceSnapshot>()];
            var seen = new HashSet<Guid>();
            foreach (var balance in balances)
            {
                if (!seen.Add(balance.AccountId))
                {
                    throw new InvalidOperationException("Duplicate account snapshot in one ledger event.");
                }

                if (await connection.ExecuteAsync(new CommandDefinition(
                        SqlScripts.ReadSqlScript("PostgreSql/Queries/Accounts/AccountStore/UpdateProjectedAccountBalances"),
                        balance,
                        transaction,
                        cancellationToken: ct)) == 1)
                {
                    await ServiceDatabase.SaveDomainEventToOutbox(
                            connection,
                            transaction,
                            DomainEvent.CreateDomainEvent(
                                "accounts",
                                EventTypes.BalanceUpdated,
                                balance,
                                value.CorrelationId,
                                value.EventId,
                                value.TransactionReference),
                            ct);
                }
            }
        },
            ct);
    }
}
