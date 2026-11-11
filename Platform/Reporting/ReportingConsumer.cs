using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Reporting.Interfaces;

namespace DisputePortal.Platform.Reporting;

public class ReportingConsumer(IReportingStore store) : IDomainEventConsumer
{
    public string[] Subscriptions => [EventTypes.LedgerTransactionPosted, EventTypes.PaymentCompleted, EventTypes.PaymentFailed];

    public Task Handle(DomainEvent e, CancellationToken ct) => store.Project(e, ct);
}
