using Dapper;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.Configuration;

public class CountryOperationStore(ServiceDatabase db)
{


    public static bool CanBegin(string caller, string sourceKind) => sourceKind switch
    {
        "Account" => caller is "identity" or "accounts",
        "Payment" => caller == "payments",
        "Reversal" => caller is "transactions" or "scheduling" or "debt" or "ledger",
        "DisputeFinance" or "Connection" => caller == "identity",
        "Import" => caller is "identity" or "ledger",
        _ => false
    };

    public async Task<CountryOperation> Begin(CountryOperationRequest request, string caller, CancellationToken ct)
    {
        if (!CanBegin(caller, request.SourceKind))
        {
            throw new PlatformException(403, "caller_denied", "This service cannot register this financial operation.");
        }

        if (request.RequestId == Guid.Empty
            || request.OperationId == Guid.Empty
            || request.OriginalTransactionId == Guid.Empty
            || request.CustomerId == Guid.Empty
            || request.SourceKind is "Account" or "Payment"
            && request.CustomerId is null)
        {
            throw new PlatformException(400, "request_required", "Use a valid request and operation identifier.");
        }

        await using var c = await db.OpenServiceDatabaseConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await c.ExecuteScalarAsync<long>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Configuration/CountryOperationStore/GetSetting"),
            transaction: tx,
            cancellationToken: ct));
        var prior = await c.QuerySingleOrDefaultAsync<CountryOperation>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Configuration/CountryOperationStore/GetCountryOperationBySourceKindAndRequestId"),
            request,
            tx,
            cancellationToken: ct));
        if (prior is not null)
        {
            var matches = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                SqlScripts.ReadSqlScript("PostgreSql/Queries/Configuration/CountryOperationStore/GetOriginalTransactionIdForRequest"),
                new
                {
                    Caller = caller,
                    request.CountryCode,
                    request.Currency,
                    request.OriginalTransactionId,
                    request.CustomerId,
                    request.SourceKind,
                    request.RequestId
                },
                tx,
                cancellationToken: ct));
            if (!matches || request.OperationId is { } supplied && supplied != prior.OperationId)
            {
                throw new PlatformException(
                    409,
                    "request_conflict",
                    "This operation request was already registered with different details.");
            }

            await tx.CommitAsync(ct);
            return prior;
        }

        var settings = await SettingsStore.GetPlatformSettings(c, tx, ct);
        if (request.ExpectedVersion is { } expected && expected != settings.Version)
        {
            throw new PlatformException(409, "settings_changed", "Country settings changed. Review the operation again.");
        }

        var historical = request.SourceKind == "Import"
            || (request.OriginalTransactionId is not null
            && request.SourceKind is "Reversal" or "DisputeFinance");
        if (historical)
        {
            if (!settings.Countries.Any(country => country.Code == request.CountryCode)
                || !settings.Currencies.Any(currency => currency.Code == request.Currency))
            {
                throw new PlatformException(409, "currency_unavailable", "The original financial currency is unavailable.");
            }
        }
        else if (!CountryCurrencyPolicy.IsCurrent(settings, request.CountryCode, request.Currency))
        {
            throw new PlatformException(
                409,
                "currency_unavailable",
                "Only the saved country's default currency is available for new activity.");
        }

        var operation = new CountryOperation
        {
            RequestId = request.RequestId,
            OperationId = request.OperationId ?? Guid.NewGuid(),
            SourceKind = request.SourceKind,
            PolicyVersion = settings.Version,
            CountryCode = request.CountryCode,
            Currency = request.Currency,
            OriginalTransactionId = request.OriginalTransactionId,
            CustomerId = request.CustomerId
        };
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Configuration/CountryOperationStore/InsertPendingCountryOperation"),
            new
            {
                operation.RequestId,
                operation.OperationId,
                operation.SourceKind,
                Caller = caller,
                operation.PolicyVersion,
                operation.CountryCode,
                operation.Currency,
                request.OriginalTransactionId,
                request.CustomerId
            },
            tx,
            cancellationToken: ct));
        await tx.CommitAsync(ct);
        return operation;
    }

    public async Task<CountryOperation> GetCountryOperation(Guid operationId, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        return await c.QueryFirstOrDefaultAsync<CountryOperation>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Configuration/CountryOperationStore/GetCountryOperationByOperationId"),
            new
            {
                OperationId = operationId
            },
            cancellationToken: ct)) ?? throw new PlatformException(
            409,
            "operation_required",
            "Register the financial operation before changing account or ledger state.");
    }

    public async Task Settle(string sourceKind, Guid requestId, string caller, CancellationToken ct, Guid? customerId = null)
    {
        if (!CanBegin(caller, sourceKind))
        {
            throw new PlatformException(403, "caller_denied", "This service cannot settle this financial operation.");
        }

        if (sourceKind is "Account" or "Payment" && customerId is null)
        {
            throw new PlatformException(400, "owner_required", "The financial operation owner is required.");
        }

        await using var connection = await db.OpenServiceDatabaseConnection(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        // Serialize cancellation with a Begin whose HTTP acknowledgement may have been lost.
        await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Configuration/CountryOperationStore/GetSetting"),
            transaction: transaction,
            cancellationToken: ct));
        // Cancellation only closes a capture. It must remain possible after removal
        // of the current settings; using retained content here never authorizes spending.
        var settings = (await SettingsVersionStore.Rows(connection, transaction, ct)).Single().Settings;
        var country = CountryCurrencyPolicy.Current(settings);
        // A cancellation can arrive before a delayed Begin. Persist its completed capture,
        // so that the late request cannot create an unfinished operation afterward.
        await connection.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Configuration/CountryOperationStore/InsertSettledCountryOperationIfMissing"),
            new
            {
                RequestId = requestId,
                OperationId = Guid.NewGuid(),
                SourceKind = sourceKind,
                Caller = caller,
                PolicyVersion = settings.Version,
                Country = country.Code,
                Currency = country.DefaultCurrency,
                CustomerId = customerId
            },
            transaction,
            cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Configuration/CountryOperationStore/UpdateCountryOperationsForCustomerAndRequest"),
            new
            {
                SourceKind = sourceKind,
                RequestId = requestId,
                Caller = caller,
                CustomerId = customerId
            },
            transaction,
            cancellationToken: ct));
        await transaction.CommitAsync(ct);
    }

    public Task CompleteCountryOperation(DomainEvent value, CancellationToken ct) => db.ProcessEventWithInboxDeduplication(
        value,
        async (c, tx) =>
    {
        if (value.EventType is "country.operation.settled" or "ledger.import.settled")
        {
            var service = value.EventType == "ledger.import.settled" ? "ledger" : "identity";
            var kind = value.EventType == "ledger.import.settled" ? "Import" : "Connection";
            if (value.SourceService != service)
            {
                throw new InvalidOperationException("Untrusted country operation settlement.");
            }

            var settlement = value.DeserializePayload<CountryOperationSettlement>();
            if (settlement.SourceKind != kind || settlement.RequestId == Guid.Empty || settlement.OperationId == Guid.Empty)
            {
                throw new InvalidOperationException("Invalid country operation settlement.");
            }

            await c.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE configuration."countryOperations"
                    SET
                        "settledAt" = COALESCE("settledAt", now())
                    WHERE "sourceService" = @SourceService
                    AND "sourceKind" = @SourceKind
                    AND "requestId" = @RequestId
                    AND "operationId" = @OperationId
                    """,
                    new
                    {
                        settlement.RequestId,
                        settlement.OperationId,
                        settlement.SourceKind,
                        SourceService = service
                    },
                    tx,
                    cancellationToken: ct));
            return;
        }

        if (value.EventType == "debt.reviewed")
        {
            if (value.Payload.GetProperty("status").GetString() == "Ineligible")
            {
                await c.ExecuteAsync(new CommandDefinition(
                        """
                        UPDATE configuration."countryOperations"
                        SET
                            "settledAt" = COALESCE("settledAt", now())
                        WHERE "sourceKind" = 'Reversal'
                        AND "sourceService" = 'scheduling'
                        AND "requestId" = @RequestId
                        """,
                        new
                        {
                            RequestId = value.Payload.GetProperty("requestId").GetGuid()
                        },
                        tx,
                        cancellationToken: ct));
            }

            return;
        }

        var operationId = value.EventType switch
        {
            EventTypes.AccountActivated => value.DeserializePayload<AccountEvent>().AccountId,
            EventTypes.PaymentCompleted or EventTypes.PaymentFailed => value.DeserializePayload<PaymentEvent>().PaymentId,
            _ => value.DeserializePayload<TransactionEvent>().TransactionId
        };
        await c.ExecuteAsync(new CommandDefinition(
                """
                UPDATE configuration."countryOperations"
                SET
                    "settledAt" = COALESCE("settledAt", now())
                WHERE "operationId" = @OperationId
                """,
                new
                {
                    OperationId = operationId
                },
                tx,
                cancellationToken: ct));
    },
        ct);
}
