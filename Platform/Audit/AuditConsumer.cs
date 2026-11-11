using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Audit.Interfaces;
using DisputePortal.Platform.Infrastructure.Interfaces;

namespace DisputePortal.Platform.Audit;

public class AuditConsumer(IAuditStore store) : IDomainEventConsumer
{
    public string[] Subscriptions => ["#"];

    public Task Handle(DomainEvent value, CancellationToken ct) => store.Append(value, ct);
}
