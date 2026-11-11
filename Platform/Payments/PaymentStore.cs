using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Payments.Interfaces;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.Payments;

public class PaymentStore(ServiceDatabase db) : IPaymentStore
{
    public static readonly string Schema = SqlScripts.ReadSqlScript("PostgreSql/Schema/Payments/PaymentStore/CreatePaymentSchema");

    private static string Hash(PaymentRequest r) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
        r,
        DomainEvent.Json))));

    private static PaymentRecord Record(Row row)
    {
        var e = JsonSerializer.Deserialize<PaymentEvent>(row.EventBody, DomainEvent.Json)!;
        return new global::DisputePortal.Platform.Contracts.PaymentRecord
        {
            Id = e.PaymentId,
            UserId = e.UserId,
            RequestId = e.RequestId,
            Kind = e.Kind,
            AccountId = e.AccountId,
            RecipientAccountId = e.RecipientAccountId,
            Amount = e.Amount,
            Currency = e.Currency,
            Fee = e.Fee,
            TargetAmount = e.TargetAmount,
            TargetCurrency = e.TargetCurrency,
            Status = row.Status,
            Reference = e.Reference,
            CreatedAt = row.CreatedAt,
            SettingsVersion = e.SettingsVersion,
            FxQuoteId = e.FxQuoteId,
            FailureCode = row.FailureCode,
            QuoteExpiresAt = e.QuoteExpiresAt
        };
    }

    public async Task SaveQuote(PaymentQuote q, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        await c.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO payments."quotes"(
                "publicId",
                "customerPublicId",
                body,
                "expiresAt"
            )
            VALUES (
                @Id,
                @UserId,
                CAST(@Body AS jsonb),
                @ExpiresAt
            )
            """,
            new
            {
                q.Id,
                q.UserId,
                Body = JsonSerializer.Serialize(q, DomainEvent.Json),
                q.ExpiresAt
            },
            cancellationToken: ct));
    }

    public async Task<PaymentRecord?> Replay(Guid user, PaymentRequest request, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        var row = await c.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Payments/PaymentStore/GetPaymentByUserAndRequestId"),
            new
            {
                User = user,
                request.RequestId
            },
            cancellationToken: ct));
        if (row is null)
        {
            return null;
        }

        if (row.RequestHash != Hash(request))
        {
            throw new PlatformException(409, "request_conflict", "This request ID was already used with different details.");
        }

        return Record(row);
    }

    public async Task<PaymentQuote> ReadQuote(Guid user, PaymentRequest request, CancellationToken ct)
    {
        await using var connection = await db.OpenServiceDatabaseConnection(ct);
        var body = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            """
            SELECT
                body::text
            FROM payments."quotes"
            WHERE "publicId" = @QuoteId
            AND "customerPublicId" = @User
            """,
            new
            {
                request.QuoteId,
                User = user
            },
            cancellationToken: ct)) ?? throw new PlatformException(409, "quote_required", "Request a payment quote first.");
        var quote = JsonSerializer.Deserialize<PaymentQuote>(body, DomainEvent.Json)!;
        PaymentPolicy.Match(request, quote, user);
        if (quote.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new PlatformException(409, "quote_expired", "Request a fresh payment quote.");
        }

        return quote;
    }

    public async Task<CountryChangeEligibility> CountryEligibility(CancellationToken ct)
    {
        await using var connection = await db.OpenServiceDatabaseConnection(ct);
        var unfinished = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Payments/PaymentStore/CheckPaymentsExistForCountryEligibility"),
            cancellationToken: ct));
        return new CountryChangeEligibility
        {
            HasUnfinishedOperations = unfinished,
            Blocker = unfinished ? "A payment is still being processed or released." : null
        };
    }

    public async Task<PaymentRecord> InsertPaymentIntent(Guid user, PaymentRequest r, CountryOperation operation, CancellationToken ct)
    {
        PaymentPolicy.RequireTransfer(r.Kind);
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(
            """
            SELECT
                pg_advisory_xact_lock(hashtext(@Id))
            """,
            new
            {
                Id = "payment-request:" + r.RequestId
            },
            tx,
            cancellationToken: ct));
        var old = await c.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Payments/PaymentStore/GetPaymentByUserAndRequestId"),
            new
            {
                User = user,
                r.RequestId
            },
            tx,
            cancellationToken: ct));
        if (old is not null)
        {
            if (old.RequestHash != Hash(r))
            {
                throw new PlatformException(409, "request_conflict", "This request ID already has different details.");
            }

            return Record(old);
        }

        if (await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Payments/PaymentStore/CheckPaymentsExistByRequestIdAndUser"),
            new
            {
                r.RequestId,
                User = user
            },
            tx,
            cancellationToken: ct)))
        {
            throw new PlatformException(409, "request_conflict", "Use a new request identifier for this payment.");
        }

        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT
                COUNT(*)
            FROM payments."cancelledRequests"
            WHERE "customerPublicId" = @User
            AND "requestId" = @Id
            """,
            new
            {
                User = user,
                Id = r.RequestId
            },
            tx,
            cancellationToken: ct)) > 0)
        {
            throw new PlatformException(409, "request_cancelled", "This payment request was cancelled. Start a new request if needed.");
        }

        var body = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Payments/PaymentStore/GetQuoteForUserByIdAndUser"),
            new
            {
                Id = r.QuoteId,
                User = user
            },
            tx,
            cancellationToken: ct)) ?? throw new PlatformException(409, "quote_required", "Request a payment quote first.");
        var q = JsonSerializer.Deserialize<PaymentQuote>(body, DomainEvent.Json)!;
        PaymentPolicy.Match(r, q, user);
        if (q.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new PlatformException(409, "quote_expired", "Request a fresh payment quote.");
        }

        if (operation.OperationId == Guid.Empty
            || operation.RequestId != r.RequestId
            || operation.CustomerId != user
            || operation.SourceKind != "Payment"
            || operation.SettledAt is not null
            || operation.PolicyVersion != q.SettingsVersion
            || operation.Currency != q.Currency)
        {
            throw new PlatformException(
                409,
                "country_operation_required",
                "A matching unfinished country-policy operation is required for a new payment.");
        }

        var id = operation.OperationId;
        var status = "Preparing";
        var value = new PaymentEvent
        {
            PaymentId = id,
            UserId = user,
            RequestId = r.RequestId,
            Kind = r.Kind,
            AccountId = r.AccountId,
            RecipientAccountId = r.RecipientAccountId,
            Amount = r.Amount,
            State = status,
            Reference = "DP-" + id.ToString("N"),
            Currency = q.Currency,
            Fee = q.Fee,
            TargetAmount = q.TargetAmount,
            TargetCurrency = q.TargetCurrency,
            FxQuoteId = q.FxQuoteId,
            SettingsVersion = q.SettingsVersion,
            QuoteExpiresAt = q.ExpiresAt
        };
        var json = JsonSerializer.Serialize(value, DomainEvent.Json);
        await c.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO payments."payments"(
                "publicId",
                "customerPublicId",
                "requestId",
                "requestHash",
                "eventBody",
                status,
                reference
            )
            VALUES (
                @Id,
                @User,
                @RequestId,
                @Hash,
                CAST(@Body AS jsonb),
                @Status,
                @Reference
            )
            """,
            new
            {
                Id = id,
                User = user,
                r.RequestId,
                Hash = Hash(r),
                Body = json,
                Status = status,
                value.Reference
            },
            tx,
            cancellationToken: ct));
        await ServiceDatabase.InsertServiceAuditEvent(c, tx, user, "PaymentRequested", id, r.Kind, ct);
        await tx.CommitAsync(ct);
        return Record(new(id, Hash(r), json, status, DateTimeOffset.UtcNow, null));
    }

    public async Task<object> GetCustomerPayments(Guid user, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        var rows = await c.QueryAsync<Row>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Payments/PaymentStore/GetPaymentByUser"),
            new
            {
                User = user
            },
            cancellationToken: ct));
        return rows.Select(Record).ToArray();
    }

    public Task CompletePaymentIntent(DomainEvent value, CancellationToken ct) => db.ProcessEventWithInboxDeduplication(
        value,
        async (c, tx) =>
    {
        var result = value.DeserializePayload<TransactionEvent>();
        var row = await c.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
                SqlScripts.ReadSqlScript("PostgreSql/Queries/Payments/PaymentStore/LockPaymentByPublicId"),
                new
                {
                    Id = result.PaymentId
                },
                tx,
                cancellationToken: ct));
        if (row is null || row.Status is "Completed" or "Failed")
        {
            return;
        }

        var status = value.EventType == EventTypes.TransactionCompleted ? "Completed" : "Failed";
        var payment = JsonSerializer.Deserialize<PaymentEvent>(row.EventBody, DomainEvent.Json)!;
        if (payment.UserId != result.UserId || payment.AccountId != result.AccountId || payment.Amount != result.Amount)
        {
            throw new InvalidOperationException("Payment outcome does not match its intent.");
        }

        await c.ExecuteAsync(new CommandDefinition(
                """
                UPDATE payments."payments"
                SET
                    status = @Status,
                    "eventBody" = CAST(@Body AS jsonb)
                WHERE "publicId" = @Id
                """,
                new
                {
                    Status = status,
                    Id = result.PaymentId,
                    Body = JsonSerializer.Serialize(
                    new global::DisputePortal.Platform.Contracts.PaymentEvent(payment) { State = status },
                    DomainEvent.Json)
                },
                tx,
                cancellationToken: ct));
        await ServiceDatabase.SaveDomainEventToOutbox(
                c,
                tx,
                DomainEvent.CreateDomainEvent(
                    "payments",
                    status == "Completed" ? EventTypes.PaymentCompleted : EventTypes.PaymentFailed,
                    new global::DisputePortal.Platform.Contracts.PaymentEvent(payment) { State = status },
                    value.CorrelationId,
                    value.EventId,
                    value.TransactionReference),
                ct);
    },
        ct);

    public async Task<PaymentRecord> Lookup(Guid user, Guid request, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        var row = await c.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Payments/PaymentStore/GetPaymentByUserAndId"),
            new
            {
                User = user,
                Id = request
            },
            cancellationToken: ct)) ?? throw new PlatformException(404, "payment_missing", "No saved payment has been found yet.");
        return Record(row);
    }

    public async Task<PaymentRequestOutcome> CancelUnknown(Guid user, Guid request, CancellationToken ct)
    {
        if (request == Guid.Empty)
        {
            throw new PlatformException(400, "request_required", "Choose a payment request.");
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
                Id = "payment-request:" + request
            },
            tx,
            cancellationToken: ct));
        var old = await c.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Payments/PaymentStore/GetPaymentByUserAndId"),
            new
            {
                User = user,
                Id = request
            },
            tx,
            cancellationToken: ct));
        if (old is not null)
        {
            await tx.CommitAsync(ct);
            return new(false, Record(old));
        }

        if (await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Payments/PaymentStore/CheckPaymentsExistByRequestIdAndUser"),
            new
            {
                RequestId = request,
                User = user
            },
            tx,
            cancellationToken: ct)))
        {
            throw new PlatformException(409, "request_conflict", "Use a new request identifier for this payment.");
        }

        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Payments/PaymentStore/InsertCancelledRequestByUserAndId"),
            new
            {
                User = user,
                Id = request
            },
            tx,
            cancellationToken: ct));
        await ServiceDatabase.InsertServiceAuditEvent(
            c,
            tx,
            user,
            "UnstartedPaymentCancelled",
            request,
            "Late submission is blocked by the request fence.",
            ct);
        await tx.CommitAsync(ct);
        return new(true, null);
    }

    public async Task<PaymentRecord> Activate(Guid id, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var row = await c.QuerySingleAsync<Row>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Payments/PaymentStore/LockPaymentByPublicId"),
            new
            {
                Id = id
            },
            tx,
            cancellationToken: ct));
        if (row.Status == "Preparing")
        {
            var value = new global::DisputePortal.Platform.Contracts.PaymentEvent(JsonSerializer.Deserialize<PaymentEvent>(
                row.EventBody,
                DomainEvent.Json)!)
            {
                State = "Authorized"
            };
            var body = JsonSerializer.Serialize(value, DomainEvent.Json);
            await c.ExecuteAsync(new CommandDefinition(
                """
                UPDATE payments."payments"
                SET
                    status = 'Authorized',
                    "eventBody" = CAST(@Body AS jsonb)
                WHERE "publicId" = @Id
                """,
                new
                {
                    Id = id,
                    Body = body
                },
                tx,
                cancellationToken: ct));
            await ServiceDatabase.SaveDomainEventToOutbox(
                c,
                tx,
                DomainEvent.CreateDomainEvent("payments", EventTypes.PaymentInitiated, value, id, reference: value.Reference),
                ct);
            await ServiceDatabase.InsertServiceAuditEvent(
                c,
                tx,
                value.UserId,
                "TransferFundsReserved",
                id,
                "Accepted quote and funds reservation recorded.",
                ct);
            row.Status = "Authorized";
            row.EventBody = body;
        }

        await tx.CommitAsync(ct);
        return Record(row);
    }

    public async Task<bool> Fail(Guid id, string code, CancellationToken ct)
    {
        await using var connection = await db.OpenServiceDatabaseConnection(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var initial = await connection.QuerySingleAsync<Row>(new CommandDefinition(
            """
            SELECT
                "publicId" AS "Id",
                "requestHash" AS RequestHash,
                "eventBody"::text AS EventBody,
                status,
                "createdAt" AS CreatedAt,
                "failureCode" AS FailureCode
            FROM payments."payments"
            WHERE "publicId" = @Id
            """,
            new
            {
                Id = id
            },
            transaction,
            cancellationToken: ct));
        var payment = Record(initial);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            SELECT
                pg_advisory_xact_lock(hashtext(@Id))
            """,
            new
            {
                Id = "payment-request:" + payment.RequestId
            },
            transaction,
            cancellationToken: ct));
        var row = await connection.QuerySingleAsync<Row>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Payments/PaymentStore/LockPaymentByPublicId"),
            new
            {
                Id = id
            },
            transaction,
            cancellationToken: ct));
        if (row.Status is not ("Preparing" or "Failed"))
        {
            await transaction.CommitAsync(ct);
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Payments/PaymentStore/InsertCancelledRequestByUserIdAndRequestId"),
            new
            {
                payment.UserId,
                payment.RequestId
            },
            transaction,
            cancellationToken: ct));
        if (row.Status == "Preparing")
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE payments."payments"
                SET
                    status = 'Failed',
                    "failureCode" = @Code
                WHERE "publicId" = @Id
                """,
                new
                {
                    Id = id,
                    Code = code
                },
                transaction,
                cancellationToken: ct));
            await ServiceDatabase.InsertServiceAuditEvent(connection, transaction, payment.UserId, "TransferRejected", id, code, ct);
        }

        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task FinishFailure(Guid id, CancellationToken ct)
    {
        await using var connection = await db.OpenServiceDatabaseConnection(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var row = await connection.QuerySingleAsync<Row>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Payments/PaymentStore/LockPaymentByPublicId"),
            new
            {
                Id = id
            },
            transaction,
            cancellationToken: ct));
        var payment = JsonSerializer.Deserialize<PaymentEvent>(row.EventBody, DomainEvent.Json)!;
        if (row.Status == "Failed" && payment.State == "Preparing")
        {
            var failed = new global::DisputePortal.Platform.Contracts.PaymentEvent(payment)
            {
                State = "Failed"
            };
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE payments."payments"
                SET
                    "eventBody" = CAST(@Body AS jsonb)
                WHERE "publicId" = @Id
                """,
                new
                {
                    Id = id,
                    Body = JsonSerializer.Serialize(failed, DomainEvent.Json)
                },
                transaction,
                cancellationToken: ct));
            await ServiceDatabase.SaveDomainEventToOutbox(
                connection,
                transaction,
                DomainEvent.CreateDomainEvent("payments", EventTypes.PaymentFailed, failed, id, reference: failed.Reference),
                ct);
        }

        await transaction.CommitAsync(ct);
    }

    public async Task<PaymentRecord[]> Preparing(CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        return (await c.QueryAsync<Row>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Payments/PaymentStore/GetPaymentPage"),
            cancellationToken: ct))).Select(Record).ToArray();
    }

    private class Row
    {
        public Guid Id { get; set; }
        public string RequestHash { get; set; } = "";
        public string EventBody { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }
        public string? FailureCode { get; set; }

        public Row()
        {
        }

        public Row(Guid id, string requestHash, string eventBody, string status, DateTimeOffset createdAt, string? failureCode)
        {
            Id = id;
            RequestHash = requestHash;
            EventBody = eventBody;
            Status = status;
            CreatedAt = createdAt;
            FailureCode = failureCode;
        }
    }
}
