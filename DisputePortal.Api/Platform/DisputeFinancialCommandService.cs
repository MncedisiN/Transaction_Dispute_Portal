using System.Data.Common;
using Dapper;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Interfaces.Finance;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Platform;

public class DisputeFinancialCommandService(IServiceClient client, IDatabaseDialect dialect) : IDisputeFinancialCommands
{
    internal static readonly string Select = SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeFinancialCommandService/GetFinancialCommands");

    public async Task<DisputeFinancialCommand> CreateFinancialCommand(
        DbConnection c,
        DbTransaction tx,
        Guid dispute,
        Guid original,
        Guid account,
        Guid user,
        string kind,
        decimal amount,
        string currency,
        Guid? actor,
        Guid request,
        string reason,
        int? policy,
        int expectedVersion,
        Guid countryClaimRequest,
        CancellationToken ct)
    {
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeFinancialCommandService/CountActiveAccountMigrations"),
            new { Id = account },
            tx,
            cancellationToken: ct)) != 1)
        {
            throw new PortalConflictException("Complete the account's approved ledger migration before posting a financial outcome.");
        }

        var old = await c.QuerySingleOrDefaultAsync<DisputeFinancialCommand>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeFinancialCommandService/GetDisputeFinancialCommandByRequestAndDisputeAndKind"),
            new
            {
                Request = request,
                Dispute = dispute,
                Kind = kind
            },
            tx,
            cancellationToken: ct));
        if (old is not null)
        {
            if (old.RequestId != request
                || old.DisputeId != dispute
                || old.ActorId != actor
                || old.Kind != kind
                || old.Reason != reason
                || old.ExpectedVersion != expectedVersion)
            {
                throw new PortalConflictException("A different financial request is already processing for this case.");
            }

            if (old.State == "Failed")
            {
                throw new PortalConflictException("The prior request failed. Review its outcome before submitting a new request reference.");
            }

            return old;
        }

        var metadata = await client.SendServiceRequest<AccountRecord>("accounts", HttpMethod.Get, "/internal/accounts/" + account, ct: ct);
        if (metadata.UserId != user || metadata.Currency != currency)
        {
            throw new PortalConflictException("The original financial request does not match its account.");
        }

        // Each SQL attempt owns a distinct internal claim. Business RequestId
        // remains stable for financial idempotency; retries never reuse a settled
        // claim. Configuration allocates the operation identity used by events.
        // This durable claim exists before the SQL command/outbox is written.
        // Country changes cannot overlook an unpublished refund. The eligibility
        // probe deliberately never waits on this transaction's SQL locks.
        var operation = await client.SendServiceRequest<CountryOperation>(
            "configuration",
            HttpMethod.Post,
            "/country-operations",
            new CountryOperationRequest
            {
                RequestId = countryClaimRequest,
                SourceKind = "DisputeFinance",
                CountryCode = metadata.CountryCode,
                Currency = currency,
                OriginalTransactionId = original
            },
            ct: ct);
        var id = operation.OperationId;
        var command = new DisputeFinancialCommand(
            id,
            request,
            dispute,
            original,
            account,
            user,
            kind,
            amount,
            currency,
            actor,
            reason,
            policy,
            expectedVersion,
            id,
            "Queued",
            null);
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeFinancialCommandService/InsertFinancialCommand"),
            command,
            tx,
            cancellationToken: ct));
        var reference = (kind == "Refund" ? "RF-" : "RR-") + id.ToString("N");
        await ServiceDatabase.SaveDomainEventToOutbox(
            c,
            tx,
            DomainEvent.CreateDomainEvent(
                "identity",
                "dispute.financial.requested",
                new DisputeFinancialRequest
                {
                    TransactionId = id,
                    RequestId = request,
                    DisputeId = dispute,
                    OriginalTransactionId = original,
                    UserId = user,
                    AccountId = account,
                    Kind = kind,
                    Amount = amount,
                    Currency = currency,
                    Reference = reference,
                    ActorId = actor
                },
                id),
            ct);
        await ServiceDatabase.InsertServiceAuditEvent(
            c,
            tx,
            actor,
            "Dispute" + kind + "Requested",
            dispute,
            "Durable financial request: " + id,
            ct);
        return command;
    }

    // The caller must have confirmed rollback before entering this method.
    // Reuse the writer fence so a concurrent retry cannot commit between the
    // absence check and settlement. Committed/uncertain work stays protected.
    public async Task ReleaseUncommitted(DbConnection c, Guid request, Guid countryClaimRequest, CancellationToken ct)
    {
        await using var transaction = await c.BeginTransactionAsync(ct);
        await RelationalDialect.LockOperations(c, transaction, dialect.OperationsLockSql, ct);
        var persisted = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT
                COUNT(*)
            FROM "disputes"."financialCommands"
            WHERE "requestId" = @Request
            """,
            new { Request = request },
            transaction,
            cancellationToken: ct));
        if (persisted == 0)
        {
            await client.SendServiceRequest<object>(
                "configuration",
                HttpMethod.Post,
                "/country-operations/DisputeFinance/" + countryClaimRequest + "/settle",
                ct: ct);
        }

        await transaction.CommitAsync(ct);
    }

    public async Task<PendingFinancialDto?> Pending(DbConnection c, Guid dispute, CancellationToken ct)
    {
        var command = await c.QuerySingleOrDefaultAsync<DisputeFinancialCommand>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeFinancialCommandService/GetDisputeFinancialCommandByDispute"),
            new { Dispute = dispute },
            cancellationToken: ct));
        return command is null || command.State == "Posted" ? null : new(
            command.TransactionId,
            command.Kind,
            command.State == "Queued" ? "Processing" : "Failed",
            command.FailureCode);
    }

    public async Task<bool> CanSettleHistorical(Guid account, Guid user, string currency, CancellationToken ct)
    {
        var metadata = await client.SendServiceRequest<AccountRecord>("accounts", HttpMethod.Get, "/internal/accounts/" + account, ct: ct);
        if (metadata.Id != account || metadata.UserId != user || metadata.Currency != currency || metadata.Status != "Blocked")
        {
            return false;
        }

        var settings = await client.SendServiceRequest<PlatformSettings>("configuration", HttpMethod.Get, "/settings", ct: ct);
        return !CountryCurrencyPolicy.IsCurrent(settings, metadata.CountryCode, metadata.Currency);
    }

    public async Task<IReadOnlyList<AccountBalanceDto>> Balances(Guid user, CancellationToken ct)
    {
        var positions = await client.SendServiceRequest<BalanceSnapshot[]>(
            "ledger",
            HttpMethod.Get,
            "/owners/" + user + "/balances",
            ct: ct);
        var metadata = await client.SendServiceRequest<AccountRecord[]>(
            "accounts",
            HttpMethod.Get,
            "/internal/owners/" + user + "/accounts",
            ct: ct);
        return metadata.Select(a =>
        {
            var p = positions.SingleOrDefault(p => p.AccountId == a.Id);
            return new AccountBalanceDto(
                a.Id,
                a.Currency,
                p?.LedgerBalance,
                p is null ? null : p.LedgerBalance - p.ReservedBalance,
                p?.ReservedBalance,
                p is not null,
                a.Status,
                a.Type);
        }).ToArray();
    }
}
