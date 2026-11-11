using Dapper;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.Accounts;

public class AccountImportStore(ServiceDatabase db, IServiceClient client)
{
    public async Task<object> Import(AccountHistorySnapshot book, CancellationToken ct)
    {
        book.ValidateAccountHistorySnapshot(); 
        var attemptId = Guid.NewGuid();
        var began = false;
        try
        {
            var settings = await client.SendServiceRequest<PlatformSettings>("configuration", HttpMethod.Get, "/settings", ct: ct);
            var eligible = CountryCurrencyPolicy.IsCurrent(settings, book.CountryCode, book.Currency);
            CountryOperation? operation = null;
            if (eligible)
            {
                began = true;
                operation = await client.SendServiceRequest<CountryOperation>(
                    "configuration",
                    HttpMethod.Post,
                    "/country-operations",
                    new CountryOperationRequest
                    {
                        RequestId = attemptId,
                        OperationId = book.AccountId,
                        SourceKind = "Account",
                        CustomerId = book.UserId,
                        CountryCode = book.CountryCode,
                        Currency = book.Currency,
                        ExpectedVersion = settings.Version
                    },
                    ct: ct);
            }

            await using var c = await db.OpenServiceDatabaseConnection(ct);
            await using var tx = await c.BeginTransactionAsync(ct);
            await c.ExecuteAsync(new CommandDefinition(
                """
                SELECT
                    pg_advisory_xact_lock(hashtext(@Id))
                """,
                new
                {
                    Id = book.UserId.ToString()
                },
                tx,
                cancellationToken: ct));
            var old = await c.QuerySingleOrDefaultAsync<(Guid UserId, string Currency, string Number, string Type, string CountryCode)?>(new CommandDefinition(
                """
                SELECT
                    "customerPublicId" AS UserId,
                    currency,
                    "accountNumber" AS Number,
                    "accountType" AS Type,
                    "countryCode" AS CountryCode
                FROM accounts."accounts"
                WHERE "publicId" = @AccountId
                """,
                book,
                tx,
                cancellationToken: ct));
            if (old is not null)
            {
                if (old.Value.UserId != book.UserId
                    || old.Value.Currency != book.Currency
                    || old.Value.Number != book.Number
                    || old.Value.Type != book.AccountType
                    || old.Value.CountryCode != book.CountryCode)
                {
                    throw new PlatformException(409, "import_conflict", "The prior account's identity differs from its existing account.");
                }

                await tx.CommitAsync(ct);
                if (began)
                {
                    await client.SendServiceRequest<object>(
                        "configuration",
                        HttpMethod.Post,
                        $"/country-operations/Account/{attemptId}/settle?customerId={book.UserId}",
                        ct: ct);
                }

                return new
                {
                    accountId = book.AccountId,
                    replayed = true
                };
            }

            if (operation is { SettledAt: not null })
            {
                throw new PlatformException(409, "operation_settled", "Review the account import again.");
            }

            await c.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("PostgreSql/Queries/Accounts/AccountImportStore/InsertImportedAccount"),
                new
                {
                    book.AccountId,
                    book.UserId,
                    book.Number,
                    book.AccountType,
                    book.CountryCode,
                    book.Currency,
                    CreatedAt = book.CreatedAt.ToUniversalTime(),
                    Status = eligible ? "PendingLedger" : "Blocked",
                    PolicyVersion = settings.Version
                },
                tx,
                cancellationToken: ct));
            if (eligible)
            {
                await ServiceDatabase.SaveDomainEventToOutbox(
                    c,
                    tx,
                    DomainEvent.CreateDomainEvent(
                        "accounts",
                        EventTypes.AccountCreated,
                        new AccountEvent
                        {
                            AccountId = book.AccountId,
                            UserId = book.UserId,
                            CountryCode = book.CountryCode,
                            Currency = book.Currency,
                            AccountType = book.AccountType
                        },
                        book.SnapshotId),
                    ct);
            }

            await ServiceDatabase.InsertServiceAuditEvent(
                c,
                tx,
                book.ActorId,
                "PriorAccountImported",
                book.AccountId,
                "Approved source book hash: " + book.Hash,
                ct);
            await tx.CommitAsync(ct);
            return new
            {
                accountId = book.AccountId,
                replayed = false
            };
        }
        catch
        {
            if (began)
            {
                using var recovery = new CancellationTokenSource(db.CleanupTimeout);
                try
                {
                    await using var connection = await db.OpenServiceDatabaseConnection(recovery.Token);
                    await using var transaction = await connection.BeginTransactionAsync(recovery.Token);
                    await connection.ExecuteAsync(new CommandDefinition(
                        """
                        SELECT
                            pg_advisory_xact_lock(hashtext(@Id))
                        """,
                        new
                        {
                            Id = book.UserId.ToString()
                        },
                        transaction,
                        cancellationToken: recovery.Token));
                    if (!await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                        SqlScripts.ReadSqlScript("PostgreSql/Queries/Accounts/AccountImportStore/CheckImportedAccountExists"),
                        book,
                        transaction,
                        cancellationToken: recovery.Token)))
                    {
                        await client.SendServiceRequest<object>(
                            "configuration",
                            HttpMethod.Post,
                            $"/country-operations/Account/{attemptId}/settle?customerId={book.UserId}",
                            ct: recovery.Token);
                    }

                    await transaction.CommitAsync(recovery.Token);
                }
                catch
                {
                }
            }

            throw;
        }
    }
}
