using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.Scheduling;

public class ScheduledReversalOutcomeConsumer(ServiceDatabase db) : IDomainEventConsumer
{
    public string[] Subscriptions => [EventTypes.TransactionCompleted, EventTypes.TransactionFailed, "debt.reviewed"];

    public Task Handle(DomainEvent value, CancellationToken ct) => db.ProcessEventWithInboxDeduplication(
        value,
        async (connection, transaction) =>
    {
        Guid requestId;
        string status;
        string? code;
        if (value.EventType == "debt.reviewed")
        {
            var result = value.Payload;
            if (result.GetProperty("status").GetString() != "Ineligible")
            {
                return;
            }

            requestId = result.GetProperty("requestId").GetGuid();
            status = "Ineligible";
            code = result.GetProperty("code").GetString();
        }
        else
        {
            var result = value.DeserializePayload<TransactionEvent>();
            requestId = result.TransactionId;
            status = result.Status;
            code = result.FailureCode;
            if (status is not ("Completed" or "Failed"))
            {
                return;
            }
        }

        var outcome = await Dapper.SqlMapper.QuerySingleOrDefaultAsync<ReversalReviewOutcome>(
                connection,
                new Dapper.CommandDefinition(
                    SqlScripts.ReadSqlScript("PostgreSql/Queries/Scheduling/ScheduledReversalOutcomeConsumer/UpdateReversalsForRequest"),
                    new
                    {
                        RequestId = requestId,
                        Status = status,
                        Code = code
                    },
                    transaction,
                    cancellationToken: ct));
        if (outcome is not null)
        {
            await ServiceDatabase.InsertServiceAuditEvent(
                    connection,
                    transaction,
                    outcome.ActorId,
                    "ReversalReviewCompleted",
                    outcome.RequestId,
                    outcome.Code ?? outcome.Status,
                    ct);
            await ServiceDatabase.SaveDomainEventToOutbox(
                    connection,
                    transaction,
                    DomainEvent.CreateDomainEvent("scheduling", "reversal.review.completed", outcome, outcome.RequestId, value.EventId),
                    ct);
        }
    },
        ct);
}
