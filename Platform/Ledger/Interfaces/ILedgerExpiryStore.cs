namespace DisputePortal.Platform.Ledger.Interfaces;

public interface ILedgerExpiryStore
{
    Task<Guid[]> ExpiredReservations(CancellationToken ct);
}
