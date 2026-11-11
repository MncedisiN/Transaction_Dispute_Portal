using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Ledger.Interfaces;

namespace DisputePortal.Platform.Ledger;

public class ReservationExpiryJob(ILedgerExpiryStore expired, ILedgerCommandStore commands) : IPersistentJobRunner
{
    public async Task RunScheduledWork(CancellationToken ct)
    {
        foreach (var id in await expired.ExpiredReservations(ct))
            await commands.ReleaseReservedAccountFunds(id, ct);
    }
}
