using DisputePortal.Platform.Contracts;

namespace DisputePortal.Platform.Ledger.Interfaces;

public interface ILedgerAccountWriter
{
    Task HandleAccountCreated(DomainEvent value, CancellationToken ct);

    Task ApplyCountryPolicy(PlatformSettings settings, CancellationToken ct);
}
