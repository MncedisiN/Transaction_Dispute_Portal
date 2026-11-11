using DisputePortal.Platform.Contracts;

namespace DisputePortal.Platform.Ledger.Interfaces;

public interface IFinancialConfiguration
{
    Task<PlatformSettings> GetFinancialConfiguration(CancellationToken ct);

    Task<CountryOperation> RequireOperation(Guid operationId, CancellationToken ct);
}
