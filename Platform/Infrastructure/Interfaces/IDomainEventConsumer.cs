using DisputePortal.Platform.Contracts;

namespace DisputePortal.Platform.Infrastructure.Interfaces;

public interface IDomainEventConsumer
{
    string[] Subscriptions { get; }

    Task Handle(DomainEvent value, CancellationToken ct);
}
