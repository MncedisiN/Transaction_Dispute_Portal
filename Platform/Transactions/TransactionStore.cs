using System.Text.Json;
using Dapper;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Transactions.Interfaces;
using DisputePortal.Platform.Transactions.Models;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.Transactions;

public class TransactionStore(ServiceDatabase db, IHistoryStore history, ReceivedActivityStore received) : ITransactionStore
{
    public static readonly string Schema = SqlScripts.ReadSqlScript("PostgreSql/Schema/Transactions/TransactionStore/CreateTransactionSchema");
    private static readonly string Select = SqlScripts.ReadSqlScript("PostgreSql/Queries/Transactions/TransactionStore/GetTransactions");

    private static TransactionRecord Record(WorkItem w)
    {
        var p = w.Payment;
        return new global::DisputePortal.Platform.Contracts.TransactionRecord
        {
            Id = w.Id,
            PaymentId = p.PaymentId,
            UserId = p.UserId,
            Kind = p.Kind,
            Reference = p.Reference,
            AccountId = p.AccountId,
            RecipientAccountId = p.RecipientAccountId,
            Amount = p.Amount,
            Currency = p.Currency,
            TargetAmount = p.TargetAmount,
            TargetCurrency = p.TargetCurrency,
            Fee = p.Fee,
            Status = w.Status,
            FailureCode = w.FailureCode,
            CreatedAt = w.CreatedAt,
            UpdatedAt = w.UpdatedAt,
            Direction = p.Kind is "Deposit" or "Refund" ? "Credit" : "Debit"
        };
    }

    private static TransactionEvent Event(WorkItem w, string status, string? code)
    {
        var r = Record(w);
        return new global::DisputePortal.Platform.Contracts.TransactionEvent
        {
            TransactionId = r.Id,
            PaymentId = r.PaymentId,
            UserId = r.UserId,
            Kind = r.Kind,
            Reference = r.Reference,
            AccountId = r.AccountId,
            RecipientAccountId = r.RecipientAccountId,
            Amount = r.Amount,
            Currency = r.Currency,
            TargetAmount = r.TargetAmount,
            TargetCurrency = r.TargetCurrency,
            Fee = r.Fee,
            Status = status,
            FailureCode = code
        };
    }

    public Task Accept(DomainEvent value, CancellationToken ct) => db.ProcessEventWithInboxDeduplication(
        value,
        async (c, tx) =>
    {
        var p = value.DeserializePayload<PaymentEvent>();
        if (p.PaymentId == Guid.Empty
                || p.UserId == Guid.Empty
                || p.RequestId == Guid.Empty
                || p.Currency.Length != 3
                || p.Amount <= 0
                || p.Kind is not ("Transfer" or "Deposit" or "Withdrawal"))
        {
            throw new InvalidOperationException("Invalid payment intent.");
        }

        var old = await c.QuerySingleOrDefaultAsync<WorkItem>(new CommandDefinition(
                SqlScripts.ReadSqlScript("PostgreSql/Queries/Transactions/TransactionStore/LockTransactionForPaymentAcceptance"),
                new
                {
                    Id = p.PaymentId
                },
                tx,
                cancellationToken: ct));
        if (old is not null)
        {
            var a = old.Payment;
            if (a.UserId != p.UserId
                    || a.Kind != p.Kind
                    || a.AccountId != p.AccountId
                    || a.RecipientAccountId != p.RecipientAccountId
                    || a.Amount != p.Amount
                    || a.Fee != p.Fee
                    || a.Currency != p.Currency
                    || a.TargetAmount != p.TargetAmount
                    || a.TargetCurrency != p.TargetCurrency
                    || a.FxQuoteId != p.FxQuoteId
                    || a.Reference != p.Reference)
            {
                throw new InvalidOperationException("Payment authorization conflicts with original intent.");
            }

            if (old.Status != "AwaitingApproval" || p.State != "Authorized")
            {
                return;
            }

            await c.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE transactions."transactions"
                    SET
                        status = 'Initiated',
                        "updatedAt" = now(),
                        "nextAttemptAt" = now()
                    WHERE "publicId" = @Id
                    """,
                    new
                    {
                        Id = p.PaymentId
                    },
                    tx,
                    cancellationToken: ct));
            await ServiceDatabase.SaveDomainEventToOutbox(
                    c,
                    tx,
                    DomainEvent.CreateDomainEvent(
                        "transactions",
                        EventTypes.TransactionInitiated,
                        Event(old, "Initiated", null),
                        p.PaymentId,
                        value.EventId,
                        p.Reference),
                    ct);
            return;
        }

        var status = p.State == "Authorized" ? "Initiated" : "AwaitingApproval";
        await c.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO transactions."transactions"(
                    "publicId",
                    "paymentPublicId",
                    "customerPublicId",
                    "paymentBody",
                    status
                )
                VALUES (
                    @Id,
                    @Id,
                    @UserId,
                    CAST(@Body AS jsonb),
                    @Status
                )
                """,
                new
                {
                    Id = p.PaymentId,
                    p.UserId,
                    Body = JsonSerializer.Serialize(p, DomainEvent.Json),
                    Status = status
                },
                tx,
                cancellationToken: ct));
        if (status == "Initiated")
        {
            await ServiceDatabase.SaveDomainEventToOutbox(
                    c,
                    tx,
                    DomainEvent.CreateDomainEvent(
                        "transactions",
                        EventTypes.TransactionInitiated,
                        new TransactionEvent
                        {
                            TransactionId = p.PaymentId,
                            PaymentId = p.PaymentId,
                            UserId = p.UserId,
                            Kind = p.Kind,
                            Reference = p.Reference,
                            AccountId = p.AccountId,
                            RecipientAccountId = p.RecipientAccountId,
                            Amount = p.Amount,
                            Currency = p.Currency,
                            TargetAmount = p.TargetAmount,
                            TargetCurrency = p.TargetCurrency,
                            Fee = p.Fee,
                            Status = status
                        },
                        p.PaymentId,
                        value.EventId,
                        p.Reference),
                    ct);
        }
    },
        ct);

    public async Task<TransactionRecord> Reverse(ReversalPreparation r, TransactionRecord original, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(
            """
            SELECT
                pg_advisory_xact_lock(hashtext(@Id))
            """,
            new
            {
                Id = r.RequestId.ToString()
            },
            tx,
            cancellationToken: ct));
        var old = await c.QuerySingleOrDefaultAsync<WorkItem>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Transactions/TransactionStore/GetClaimedTransaction"),
            new
            {
                Id = r.RequestId
            },
            tx,
            cancellationToken: ct));
        if (old is not null)
        {
            if (old.Payment.OriginalTransactionId != r.OriginalTransactionId || old.Payment.Kind != r.Kind)
            {
                throw new PlatformException(409, "request_conflict", "Reversal request details differ.");
            }

            return Record(old);
        }

        var p = new PaymentEvent
        {
            PaymentId = r.RequestId,
            UserId = original.UserId,
            RequestId = r.RequestId,
            Kind = r.Kind,
            AccountId = original.AccountId,
            RecipientAccountId = null,
            Amount = original.Amount,
            State = "Authorized",
            Reference = "DP-" + r.RequestId.ToString("N"),
            Currency = original.Currency,
            Fee = 0,
            OriginalTransactionId = r.OriginalTransactionId
        };
        await c.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO transactions."transactions"(
                "publicId",
                "paymentPublicId",
                "customerPublicId",
                "paymentBody",
                status
            )
            VALUES (
                @Id,
                @Id,
                @UserId,
                CAST(@Body AS jsonb),
                'Initiated'
            )
            """,
            new
            {
                Id = r.RequestId,
                p.UserId,
                Body = JsonSerializer.Serialize(p, DomainEvent.Json)
            },
            tx,
            cancellationToken: ct));
        var w = new WorkItem(
            r.RequestId,
            JsonSerializer.Serialize(p, DomainEvent.Json),
            "Initiated",
            Guid.Empty,
            0,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        await ServiceDatabase.SaveDomainEventToOutbox(
            c,
            tx,
            DomainEvent.CreateDomainEvent("transactions", EventTypes.TransactionInitiated, Event(w, "Initiated", null), w.Id),
            ct);
        await ServiceDatabase.InsertServiceAuditEvent(c, tx, r.ActorId, "ReversalRequested", w.Id, r.Kind, ct);
        await tx.CommitAsync(ct);
        return Record(w);
    }

    public async Task AcceptDispute(DomainEvent value, CancellationToken ct)
    {
        var r = value.DeserializePayload<DisputeFinancialRequest>();
        var original = await GetTransactionById(r.OriginalTransactionId, null, ct);
        if (r.TransactionId == Guid.Empty
            || r.RequestId == Guid.Empty
            || r.DisputeId == Guid.Empty
            || original.Status != "Completed"
            || original.UserId != r.UserId
            || original.AccountId != r.AccountId
            || original.Amount != r.Amount
            || original.Currency != r.Currency
            || r.Kind is not ("Refund" or "Recall")
            || r.Kind == "Refund"
            && original.Direction != "Debit"
            || r.Kind == "Recall"
            && original.Kind != "Refund")
        {
            throw new InvalidOperationException("The case instruction does not match its original confirmed transaction.");
        }

        await db.ProcessEventWithInboxDeduplication(
            value,
            async (c, tx) =>
        {
            await c.ExecuteAsync(new CommandDefinition(
                    """
                    SELECT
                        pg_advisory_xact_lock(hashtext(@Id))
                    """,
                    new
                    {
                        Id = r.TransactionId.ToString()
                    },
                    tx,
                    cancellationToken: ct));
            var old = await c.QuerySingleOrDefaultAsync<WorkItem>(new CommandDefinition(
                    SqlScripts.ReadSqlScript("PostgreSql/Queries/Transactions/TransactionStore/GetClaimedTransaction"),
                    new
                    {
                        Id = r.TransactionId
                    },
                    tx,
                    cancellationToken: ct));
            if (old is not null)
            {
                var p = old.Payment;
                if (p.OriginalTransactionId != r.OriginalTransactionId || p.UserId != r.UserId || p.Amount != r.Amount || p.Kind != r.Kind)
                {
                    throw new InvalidOperationException("Conflicting financial instruction.");
                }

                return;
            }

            var instruction = new PaymentEvent
            {
                PaymentId = r.TransactionId,
                UserId = r.UserId,
                RequestId = r.RequestId,
                Kind = r.Kind,
                AccountId = r.AccountId,
                RecipientAccountId = null,
                Amount = r.Amount,
                State = "Authorized",
                Reference = r.Reference,
                Currency = r.Currency,
                Fee = 0,
                OriginalTransactionId = r.OriginalTransactionId
            };
            var work = new WorkItem(
                    r.TransactionId,
                    JsonSerializer.Serialize(instruction, DomainEvent.Json),
                    "Initiated",
                    Guid.Empty,
                    0,
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow);
            await c.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO transactions."transactions"(
                        "publicId",
                        "paymentPublicId",
                        "customerPublicId",
                        "paymentBody",
                        status
                    )
                    VALUES (
                        @Id,
                        @Id,
                        @UserId,
                        CAST(@Body AS jsonb),
                        'Initiated'
                    )
                    """,
                    new
                    {
                        Id = r.TransactionId,
                        r.UserId,
                        Body = work.PaymentBody
                    },
                    tx,
                    cancellationToken: ct));
            await ServiceDatabase.SaveDomainEventToOutbox(
                    c,
                    tx,
                    DomainEvent.CreateDomainEvent(
                        "transactions",
                        EventTypes.TransactionInitiated,
                        Event(work, "Initiated", null),
                        r.TransactionId,
                        value.EventId,
                        r.Reference),
                    ct);
            await ServiceDatabase.InsertServiceAuditEvent(c, tx, r.ActorId, "CaseFinancialInstructionAccepted", r.TransactionId, r.Kind, ct);
        },
            ct);
    }

    public async Task<WorkItem?> Claim(CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        var lease = Guid.NewGuid();
        var id = await c.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Transactions/TransactionStore/ClaimNextProcessableTransaction"),
            new
            {
                Lease = lease
            },
            cancellationToken: ct));
        return id is null ? null : await c.QuerySingleAsync<WorkItem>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Transactions/TransactionStore/GetClaimedTransaction"),
            new
            {
                Id = id
            },
            cancellationToken: ct));
    }

    public async Task Transition(WorkItem w, string status, string? code, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        if (await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Transactions/TransactionStore/TransitionClaimedTransactionStatus"),
            new
            {
                w.Id,
                w.LeaseId,
                Old = w.Status,
                Status = status,
                Code = code
            },
            tx,
            cancellationToken: ct)) != 1)
        {
            return;
        }

        var type = status switch
        {
            "Validated" => EventTypes.TransactionValidated,
            "Processing" => EventTypes.TransactionProcessing,
            "Completed" => EventTypes.TransactionCompleted,
            "Failed" => EventTypes.TransactionFailed,
            _ => null
        };
        if (type is not null)
        {
            await ServiceDatabase.SaveDomainEventToOutbox(
                c,
                tx,
                DomainEvent.CreateDomainEvent("transactions", type, Event(w, status, code), w.Id, reference: w.Payment.Reference),
                ct);
        }

        if (status is "Completed" or "Failed")
        {
            await ServiceDatabase.InsertServiceAuditEvent(
                c,
                tx,
                w.Payment.UserId,
                "Transaction" + status,
                w.Id,
                code ?? "Posted ledger journal confirmed.",
                ct);
        }

        await tx.CommitAsync(ct);
    }

    public async Task RetryTransactionProcessing(WorkItem w, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        await c.ExecuteAsync(new CommandDefinition(
            """
            UPDATE transactions."transactions"
            SET
                "leaseId" = NULL,
                "leaseUntil" = NULL,
                attempts = attempts + 1,
                "nextAttemptAt" = now() + @Delay * interval '1 second'
            WHERE "publicId" = @Id
            AND "leaseId" = @LeaseId
            """,
            new
            {
                w.Id,
                w.LeaseId,
                Delay = Math.Min(300, Math.Pow(2, Math.Min(w.Attempts, 8)))
            },
            cancellationToken: ct));
    }

    public async Task<object> GetCustomerTransactions(Guid user, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        return (await c.QueryAsync<WorkItem>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Transactions/TransactionStore/GetTransactionByUser"),
            new
            {
                User = user
            },
            cancellationToken: ct))).Select(Record).Concat(await history.GetCustomerTransactionHistory(user, ct)).Concat(await received.GetReceivedAccountActivity(
            user,
            ct)) // Match Read's authoritative-source precedence before sorting or limiting.
        // Refunds and the two account legs of a transfer have distinct identities.
        .DistinctBy(transaction => transaction.Id).OrderByDescending(transaction => transaction.CreatedAt).ThenByDescending(transaction => transaction.Id).Take(250).ToArray();
    }

    public async Task<TransactionRecord> GetTransactionById(Guid id, Guid? user, CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        var row = await c.QuerySingleOrDefaultAsync<WorkItem>(new CommandDefinition(
            SqlScripts.ReadSqlScript("PostgreSql/Queries/Transactions/TransactionStore/GetTransactionByIdAndUser"),
            new
            {
                Id = id,
                User = user
            },
            cancellationToken: ct));
        if (row is null)
        {
            return await history.GetTransactionHistoryEntry(id, user, ct) ?? await received.GetReceivedActivityById(id, user, ct) ?? throw new PlatformException(
                404,
                "transaction_missing",
                "Transaction not found.");
        }

        return Record(row);
    }
}
