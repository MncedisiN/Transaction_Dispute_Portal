using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Ledger.Domain;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Ledger.Interfaces;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.Ledger;

public class PostgresLedgerRepository(ServiceDatabase db, IFinancialConfiguration configuration) : ILedgerCommandStore, ILedgerQueryStore, ILedgerAccountWriter, ILedgerExpiryStore
{



    private static BalanceSnapshot Snapshot(LedgerPosition p) => new global::DisputePortal.Platform.Contracts.BalanceSnapshot
    {
        AccountId = p.AccountId,
        UserId = p.UserId,
        Currency = p.Currency,
        LedgerBalance = p.Balance,
        ReservedBalance = p.Reserved,
        Version = p.Version
    };

    public async Task HandleAccountCreated(DomainEvent value, CancellationToken ct)
    {
        var account = value.DeserializePayload<AccountEvent>();
        if (account.AccountId == Guid.Empty || account.UserId == Guid.Empty)
        {
            throw new InvalidOperationException("Invalid account event.");
        }

        await using (var connection = await db.OpenServiceDatabaseConnection(ct))
        {
            if (await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                db.Dialect.FindProcessed,
                new { Id = value.EventId, Consumer = "ledger" },
                cancellationToken: ct)) is not null)
            {
                return;
            }
        }

        var settings = await configuration.GetFinancialConfiguration(ct);
        var eligible = CountryCurrencyPolicy.IsCurrent(settings, account.CountryCode, account.Currency);
        CountryOperation? operation = null;
        if (eligible)
        {
            operation = await configuration.RequireOperation(account.AccountId, ct);
            LedgerOperationPolicy.RequireAccount(operation, account);
        }

        await db.ProcessEventWithInboxDeduplication(
            value,
            async (c, tx) =>
        {
            var old = await c.QuerySingleOrDefaultAsync<LedgerPosition>(new CommandDefinition(
                    SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/LockLedgerPositionForReservation"),
                    new
                    {
                        Id = account.AccountId
                    },
                    tx,
                    cancellationToken: ct));
            if (old is not null
                    && (old.UserId != account.UserId
                    || old.Currency != account.Currency
                    || old.CountryCode != account.CountryCode))
            {
                throw new InvalidOperationException("Account ownership/currency conflict.");
            }

            if (!eligible)
            {
                await c.ExecuteAsync(new CommandDefinition(
                        """
                        UPDATE ledger."accounts"
                        SET
                            status = 'Blocked'
                        WHERE "publicId" = @Id
                        AND kind = 'Customer'
                        AND status = 'Active'
                        AND "countryPolicyVersion" <= @PolicyVersion
                        """,
                        new
                        {
                            Id = account.AccountId,
                            PolicyVersion = settings.Version
                        },
                        tx,
                        cancellationToken: ct));
                return;
            }

            if (old is { Status: "Blocked" or "Closed" })
            {
                return;
            }

            await c.ExecuteAsync(new CommandDefinition(
                    SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/UpsertAccount"),
                    new
                    {
                        account.AccountId,
                        account.UserId,
                        account.Currency,
                        account.CountryCode,
                        PolicyVersion = operation!.PolicyVersion
                    },
                    tx,
                    cancellationToken: ct));
            await ServiceDatabase.SaveDomainEventToOutbox(
                    c,
                    tx,
                    DomainEvent.CreateDomainEvent("ledger", "ledger.account.created", account, value.CorrelationId, value.EventId),
                    ct);
        },
            ct);
    }

    public async Task ApplyCountryPolicy(PlatformSettings settings, CancellationToken ct)
    {
        var country = CountryCurrencyPolicy.Current(settings);
        await using var connection = await db.OpenServiceDatabaseConnection(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/UpdateAccounts"),
            new
            {
                Country = country.Code,
                Currency = country.DefaultCurrency,
                PolicyVersion = settings.Version
            },
            cancellationToken: ct));
    }

    public async Task<BalanceSnapshot> ReserveAccountFunds(ReserveRequest request, PlatformSettings config, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await Lock(c, tx, request.TransactionId, ct);
        await EnsureNotAborted(c, tx, request.TransactionId, ct);
        var p = await c.QuerySingleOrDefaultAsync<LedgerPosition>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/LockLedgerPositionForReservation"),
            new
            {
                Id = request.AccountId
            },
            tx,
            cancellationToken: ct)) ?? throw new PlatformException(404, "account_missing", "Account not found.");
        if (p.UserId != request.UserId || p.Kind != "Customer" || p.Status != "Active")
        {
            throw new PlatformException(409, "account_unavailable", "The account is unavailable.");
        }

        if (!CountryCurrencyPolicy.IsCurrent(config, p.CountryCode, p.Currency))
        {
            throw new PlatformException(
                409,
                "country_currency_unavailable",
                "The account does not use the saved country's default currency.");
        }

        var old = await c.QuerySingleOrDefaultAsync<Reservation>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/GetReservationForReplay"),
            new
            {
                Id = request.TransactionId
            },
            tx,
            cancellationToken: ct));
        if (old is not null)
        {
            if (old.AccountId != p.InternalId || old.UserId != request.UserId || old.Amount != request.Amount + request.Fee)
            {
                throw new PlatformException(409, "request_conflict", "Reservation details differ.");
            }

            if (old.State == "Released")
            {
                throw new PlatformException(409, "reservation_released", "The reservation has expired or was released.");
            }

            await tx.CommitAsync(ct);
            return Snapshot(p);
        }

        if (request.SettingsVersion > 0 && request.SettingsVersion != config.Version)
        {
            throw new PlatformException(409, "policy_changed", "Transfer rules changed. Review a new quote.");
        }

        var country = CountryCurrencyPolicy.Current(config);
        var currency = config.Currencies.Single(v => v.Code == p.Currency);
        var rule = (config.FinancialRules.SingleOrDefault(v => v.Country == p.CountryCode
            && v.Currency == p.Currency) ?? throw new PlatformException(409, "policy_unavailable", "The financial policy is unavailable."));
        if (request.Amount < rule.Minimum || request.Amount > rule.Maximum)
        {
            throw new PlatformException(400, "financial_policy", "The amount is outside the configured limits.");
        }

        var fee = decimal.Round(rule.FixedFee + request.Amount * rule.PercentageFee / 100, currency.Decimals, MidpointRounding.ToEven);
        if (request.TransactionId == Guid.Empty
            || request.Amount < rule.Minimum
            || request.Amount > rule.Maximum
            || decimal.Round(request.Amount, currency.Decimals) != request.Amount
            || request.Fee != fee)
        {
            throw new PlatformException(400, "financial_policy", "The amount or fee does not match the configured policy.");
        }

        var total = request.Amount + request.Fee;
        if (p.Balance - p.Reserved < total)
        {
            throw new PlatformException(409, "insufficient_funds", "Insufficient funds for this transfer.");
        }

        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById(country.TimeZone)).DateTime);
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/InsertDailyUsage"),
            new
            {
                request.UserId,
                p.Currency,
                Day = day
            },
            tx,
            cancellationToken: ct));
        var used = await c.QuerySingleAsync<(decimal Spent, decimal Held)>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/GetDailyUsageForUser"),
            new
            {
                request.UserId,
                p.Currency,
                Day = day
            },
            tx,
            cancellationToken: ct));
        if (used.Spent + used.Held + total > rule.DailyLimit)
        {
            throw new PlatformException(409, "daily_limit", "The configured daily transaction limit would be exceeded.");
        }

        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/UpdateDailyUsageForUserAndTransaction"),
            new
            {
                request.TransactionId,
                request.AccountId,
                request.UserId,
                p.InternalId,
                p.Currency,
                Total = total,
                Day = day,
                Expires = DateTimeOffset.UtcNow.AddSeconds(config.ReservationSeconds),
                request.Reference
            },
            tx,
            cancellationToken: ct));
        p = new global::DisputePortal.Platform.Ledger.Domain.LedgerPosition(p)
        {
            Reserved = p.Reserved + total,
            Version = p.Version + 1
        };
        await ServiceDatabase.SaveDomainEventToOutbox(
            c,
            tx,
            DomainEvent.CreateDomainEvent(
                "ledger",
                "ledger.reservation.updated",
                Snapshot(p),
                correlation: request.TransactionId,
                reference: request.Reference),
            ct);
        await tx.CommitAsync(ct);
        return Snapshot(p);
    }

    public async Task<JournalResult> Post(PostingPlan plan, PlatformSettings config, CancellationToken ct)
    {
        var request = plan.Request;
        if (request.TransactionId == Guid.Empty
            || request.UserId == Guid.Empty
            || request.Amount <= 0
            || request.Fee < 0
            || request.Reference.Length is < 8 or > 100
            || request.Kind is not ("Transfer" or "Deposit" or "Withdrawal" or "Refund" or "Recall"))
        {
            throw new PlatformException(400, "invalid_posting", "Invalid ledger posting request.");
        }

        var body = JsonSerializer.Serialize(request, DomainEvent.Json);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(
            """
            SELECT
                pg_advisory_xact_lock(hashtext(@Id))
            """,
            new
            {
                Id = request.TransactionId.ToString()
            },
            tx,
            cancellationToken: ct));
        var existing = await c.QuerySingleOrDefaultAsync<Journal>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/GetJournalByTransactionId"),
            new
            {
                Id = request.TransactionId
            },
            tx,
            cancellationToken: ct));
        if (existing is not null)
        {
            if (existing.RequestHash != hash)
            {
                throw new PlatformException(409, "request_conflict", "This transaction already has different ledger details.");
            }

            var result = await Result(c, tx, existing, true, ct);
            await tx.CommitAsync(ct);
            return result;
        }

        await EnsureNotAborted(c, tx, request.TransactionId, ct);
        if (request.Kind == "Transfer"
            && (request.QuoteId is not null
            || request.TargetCurrency != request.Currency
            || request.TargetAmount != request.Amount))
        {
            throw new PlatformException(
                409,
                "cross_currency_transfer_unsupported",
                "Transfers must retain the same amount and saved default currency.");
        }

        var account = await c.QuerySingleOrDefaultAsync<LedgerPosition>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/GetLedgerPositionForPosting"),
            new
            {
                Id = request.AccountId
            },
            tx,
            cancellationToken: ct)) ?? throw new PlatformException(404, "account_missing", "Account not found.");
        var historicalSettlement = request.Kind is "Refund" or "Recall";
        if (historicalSettlement)
        {
            LedgerAccountPolicy.RequireHistoricalSource(account, request);
        }
        else
        {
            LedgerAccountPolicy.RequireCurrentSource(account, request, config);
        }

        var sourceCurrency = config.Currencies.SingleOrDefault(v => v.Code == request.Currency) ?? throw new PlatformException(
            409,
            "currency_unavailable",
            "The original currency definition is unavailable.");
        if (decimal.Round(request.Amount, sourceCurrency.Decimals) != request.Amount
            || decimal.Round(request.Fee, sourceCurrency.Decimals) != request.Fee)
        {
            throw new PlatformException(400, "invalid_precision", "Use the configured currency precision.");
        }

        var lines = plan.Lines;
        foreach (var clearing in plan.ClearingAccounts)
            await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/InsertAccount"),
            new
            {
                clearing.Id,
                Owner = Guid.Empty,
                clearing.Currency,
                Country = account.CountryCode
            },
            tx,
            cancellationToken: ct));
        if (request.Kind is "Refund" or "Recall")
        {
            await ValidateAndLinkReversal(c, tx, request, ct);
        }

        if (lines.GroupBy(v => v.Currency).Any(g => g.Sum(v => v.Side == "D" ? v.Amount : -v.Amount) != 0))
        {
            throw new InvalidOperationException("Unbalanced posting.");
        }

        var positions = (await c.QueryAsync<LedgerPosition>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/GetPostgresLedgerByIds"),
            new
            {
                Ids = new HashSet<Guid>(lines.Select(l => l.AccountId)).Order().ToArray()
            },
            tx,
            cancellationToken: ct))).ToDictionary(p => p.AccountId);
        foreach (var position in positions.Values.Where(p => p.Kind == "Customer"))
        {
            if (historicalSettlement)
            {
                LedgerAccountPolicy.RequireHistoricalSource(position, request);
            }
            else if (position.UserId != request.UserId
                || position.Status != "Active"
                || position.Currency != request.Currency
                || !CountryCurrencyPolicy.IsCurrent(config, position.CountryCode, position.Currency))
            {
                throw new PlatformException(
                    409,
                    "account_unavailable",
                    "A participating account is unavailable under the saved country policy.");
            }
        }

        if (!historicalSettlement && request.SettingsVersion > 0 && request.SettingsVersion != config.Version)
        {
            throw new PlatformException(409, "policy_changed", "Transfer rules changed. Review a new quote.");
        }

        if (request.Kind is "Deposit" or "Withdrawal" or "Transfer")
        {
            if (!CountryCurrencyPolicy.IsCurrent(config, account.CountryCode, account.Currency))
            {
                throw new PlatformException(
                    409,
                    "country_currency_unavailable",
                    "The account does not use the saved country's default currency.");
            }

            var rule = config.FinancialRules.SingleOrDefault(v => v.Country == account.CountryCode
                && v.Currency == account.Currency) ?? throw new PlatformException(
                409,
                "policy_unavailable",
                "The financial policy is unavailable.");
            if (request.Amount < rule.Minimum
                || request.Amount > rule.Maximum
                || request.Fee != decimal.Round(
                rule.FixedFee + request.Amount * rule.PercentageFee / 100,
                sourceCurrency.Decimals,
                MidpointRounding.ToEven))
            {
                throw new PlatformException(409, "financial_policy", "The configured financial policy has changed. Request a new quote.");
            }
        }

        Reservation? reservation = null;
        if (plan.RequiresReservation)
        {
            reservation = await c.QuerySingleOrDefaultAsync<Reservation>(new CommandDefinition(
                SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/LockReservationForPosting"),
                new
                {
                    Id = request.TransactionId
                },
                tx,
                cancellationToken: ct));
            if (reservation is null
                || reservation.State != "Held"
                || reservation.ExpiresAt <= DateTimeOffset.UtcNow
                || reservation.AccountId != account.InternalId
                || reservation.Amount != request.Amount + request.Fee)
            {
                throw new PlatformException(409, "reservation_invalid", "The funds reservation is unavailable or expired.");
            }

            await c.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/MarkReservationsPostedForUserAndTransaction"),
                reservation,
                tx,
                cancellationToken: ct));
        }

        var released = reservation is null ? new Dictionary<Guid, decimal>() : new Dictionary<Guid, decimal>
        {
            {
                request.AccountId,
                reservation.Amount
            }
        };
        if (request.Kind == "Recall"
            && positions[request.AccountId].Balance - positions[request.AccountId].Reserved < request.Amount)
        {
            throw new PlatformException(409, "insufficient_funds", "There are not enough available funds for this recall.");
        }

        var posted = await JournalWriter.Persist(c, tx, plan, positions, released, DateTimeOffset.UtcNow, ct);
        await tx.CommitAsync(ct);
        return posted;
    }

    private static async Task<JournalResult> Result(DbConnection c, DbTransaction? tx, Journal journal, bool replayed, CancellationToken ct)
    {
        var positions = await c.QueryAsync<LedgerPosition>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/GetPostgresLedgerByInternalId"),
            new
            {
                journal.InternalId
            },
            tx,
            cancellationToken: ct));
        return new(journal.Id, journal.TransactionId, journal.Reference, positions.Select(Snapshot).ToArray(), replayed);
    }

    public async Task ReleaseReservedAccountFunds(Guid id, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await Lock(c, tx, id, ct);
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/InsertAbort"),
            new
            {
                Id = id
            },
            tx,
            cancellationToken: ct));
        var accountId = await c.ExecuteScalarAsync<long?>(new CommandDefinition(
            """
            SELECT
                "accountId"
            FROM ledger."reservations"
            WHERE "publicId" = @Id
            """,
            new
            {
                Id = id
            },
            tx,
            cancellationToken: ct));
        if (accountId is null)
        {
            await tx.CommitAsync(ct);
            return;
        }

        var p = await c.QuerySingleAsync<LedgerPosition>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/LockLedgerPositionByInternalId"),
            new
            {
                Id = accountId
            },
            tx,
            cancellationToken: ct));
        var r = await c.QuerySingleAsync<Reservation>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/LockReservationForPosting"),
            new
            {
                Id = id
            },
            tx,
            cancellationToken: ct));
        if (r.State == "Held")
        {
            await c.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/MarkReservationsReleasedForUserAndAccountAndTransaction"),
                r,
                tx,
                cancellationToken: ct));
            await ServiceDatabase.SaveDomainEventToOutbox(
                c,
                tx,
                DomainEvent.CreateDomainEvent(
                    "ledger",
                    "ledger.reservation.updated",
                    Snapshot(new global::DisputePortal.Platform.Ledger.Domain.LedgerPosition(p)
                    {
                        Reserved = p.Reserved - r.Amount,
                        Version = p.Version + 1
                    }),
                    correlation: r.TransactionId,
                    reference: r.Reference),
                ct);
        }

        await tx.CommitAsync(ct);
    }

    public async Task<Guid[]> ExpiredReservations(CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        return (await c.QueryAsync<Guid>(new CommandDefinition(
            """
            SELECT
                "publicId" AS "Id"
            FROM ledger."reservations"
            WHERE state = 'Held'
            AND "expiresAt" < now()
            ORDER BY "expiresAt"
            LIMIT 20
            """,
            cancellationToken: ct))).ToArray();
    }

    public async Task<LedgerPosition> ReadPosition(Guid id, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        return await c.QuerySingleOrDefaultAsync<LedgerPosition>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/GetLedgerPositionForPosting"),
            new
            {
                Id = id
            },
            cancellationToken: ct)) ?? throw new PlatformException(404, "account_missing", "Account not found.");
    }

    public async Task<JournalResult?> TryPosted(PostJournalRequest request, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        var row = await c.QuerySingleOrDefaultAsync<Journal>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/GetPostedJournalForReplay"),
            new
            {
                Id = request.TransactionId
            },
            cancellationToken: ct));
        if (row is null)
        {
            return null;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, DomainEvent.Json))));
        if (row.RequestHash != hash)
        {
            throw new PlatformException(409, "request_conflict", "This transaction already has different ledger details.");
        }

        return await Result(c, null, row, true, ct);
    }

    public async Task<JournalResult> GetJournal(Guid id, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await Lock(c, tx, id, ct);
        var row = await c.QuerySingleOrDefaultAsync<Journal>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/GetPostedJournalForReplay"),
            new
            {
                Id = id
            },
            tx,
            cancellationToken: ct)) ?? throw new PlatformException(404, "journal_missing", "No posted journal exists for this transaction.");
        var result = await Result(c, tx, row, true, ct);
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task<object> Entries(Guid id, Guid user, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        var owner = await c.QuerySingleOrDefaultAsync<(long Id, Guid UserId)?>(new CommandDefinition(
            """
            SELECT
                id,
                "customerPublicId" AS UserId
            FROM ledger."accounts"
            WHERE "publicId" = @Id
            """,
            new
            {
                Id = id
            },
            cancellationToken: ct));
        if (owner is null || owner.Value.UserId != user)
        {
            throw new PlatformException(404, "account_missing", "Account not found.");
        }

        return await c.QueryAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/GetLatestPostedEntries"),
            new
            {
                Id = owner.Value.Id
            },
            cancellationToken: ct));
    }

    public async Task<object> Reconcile(CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        var invalid = await c.QueryAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/CalculateAccounts"),
            cancellationToken: ct));
        var unbalanced = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/CountUnbalancedJournalCurrencies"),
            cancellationToken: ct));
        return new
        {
            checkedAt = DateTimeOffset.UtcNow,
            positionDifferences = invalid,
            unbalancedJournals = unbalanced
        };
    }

    private static Task Lock(DbConnection c, DbTransaction tx, Guid id, CancellationToken ct) => c.ExecuteAsync(new CommandDefinition(
        """
        SELECT
            pg_advisory_xact_lock(hashtext(@Id))
        """,
        new
        {
            Id = id.ToString()
        },
        tx,
        cancellationToken: ct));

    private static async Task EnsureNotAborted(DbConnection c, DbTransaction tx, Guid id, CancellationToken ct)
    {
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT
                COUNT(*)
            FROM ledger."aborts"
            WHERE "publicId" = @Id
            """,
            new
            {
                Id = id
            },
            tx,
            cancellationToken: ct)) != 0)
        {
            throw new PlatformException(409, "transaction_aborted", "This transaction has been cancelled.");
        }
    }

    private static async Task ValidateAndLinkReversal(DbConnection c, DbTransaction tx, PostJournalRequest request, CancellationToken ct)
    {
        var original = await c.QuerySingleOrDefaultAsync<Journal>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/LockPostedJournalForReversal"),
            new
            {
                Id = request.OriginalTransactionId
            },
            tx,
            cancellationToken: ct)) ?? throw new PlatformException(
            409,
            "original_missing",
            "The original posted transaction was not found.");
        var old = JsonSerializer.Deserialize<PostJournalRequest>(original.RequestBody, DomainEvent.Json)!;
        ReversalPolicy.ValidateReversalRequest(request, old);
        if (request.Kind == "Refund")
        {
            if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT
                    COUNT(*)
                FROM ledger."refunds"
                WHERE "originalTransactionPublicId" = @Id
                AND state = 'Active'
                """,
                new
                {
                    Id = request.OriginalTransactionId
                },
                tx,
                cancellationToken: ct)) > 0)
            {
                throw new PlatformException(409, "already_refunded", "The original transaction already has an active refund.");
            }

            await c.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("PostgreSql/Queries/Ledger/PostgresLedgerRepository/UpsertRefund"),
                new
                {
                    Original = request.OriginalTransactionId,
                    Current = request.TransactionId
                },
                tx,
                cancellationToken: ct));
        }
        else
        {
            await c.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO ledger."reversals"(
                    "originalTransactionPublicId",
                    "reversalTransactionPublicId"
                )
                VALUES (
                    @Original,
                    @Current
                )
                """,
                new
                {
                    Original = request.OriginalTransactionId,
                    Current = request.TransactionId
                },
                tx,
                cancellationToken: ct));
            await c.ExecuteAsync(new CommandDefinition(
                """
                UPDATE ledger."refunds"
                SET
                    state = 'Recalled'
                WHERE "refundTransactionPublicId" = @Id
                """,
                new
                {
                    Id = request.OriginalTransactionId
                },
                tx,
                cancellationToken: ct));
        }
    }

    private class Reservation
    {
        public Guid TransactionId { get; set; }
        public long AccountId { get; set; }
        public Guid UserId { get; set; }
        public string Currency { get; set; } = "";
        public decimal Amount { get; set; }
        public DateOnly UsageDay { get; set; }
        public string State { get; set; } = "";
        public DateTimeOffset ExpiresAt { get; set; }
        public string Reference { get; set; } = "";

        public Reservation()
        {
        }

        public Reservation(
            Guid transactionId,
            long accountId,
            Guid userId,
            string currency,
            decimal amount,
            DateOnly usageDay,
            string state,
            DateTimeOffset expiresAt,
            string reference)
        {
            TransactionId = transactionId;
            AccountId = accountId;
            UserId = userId;
            Currency = currency;
            Amount = amount;
            UsageDay = usageDay;
            State = state;
            ExpiresAt = expiresAt;
            Reference = reference;
        }
    }

    private class Journal
    {
        public Guid Id { get; set; }
        public Guid TransactionId { get; set; }
        public string Reference { get; set; } = "";
        public string Kind { get; set; } = "";
        public string RequestHash { get; set; } = "";
        public string RequestBody { get; set; } = "";
        public string Status { get; set; } = "";
        public long InternalId { get; set; }

        public Journal()
        {
        }

        public Journal(
            Guid id,
            Guid transactionId,
            string reference,
            string kind,
            string requestHash,
            string requestBody,
            string status,
            long internalId)
        {
            Id = id;
            TransactionId = transactionId;
            Reference = reference;
            Kind = kind;
            RequestHash = requestHash;
            RequestBody = requestBody;
            Status = status;
            InternalId = internalId;
        }
    }
}
