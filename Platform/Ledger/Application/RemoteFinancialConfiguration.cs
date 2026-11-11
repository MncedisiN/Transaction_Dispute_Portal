using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Ledger.Interfaces;

namespace DisputePortal.Platform.Ledger;

public class RemoteFinancialConfiguration(IServiceClient client) : IFinancialConfiguration
{
    public Task<PlatformSettings> GetFinancialConfiguration(CancellationToken ct) => client.SendServiceRequest<PlatformSettings>(
        "configuration",
        HttpMethod.Get,
        "/settings",
        ct: ct);

    public Task<CountryOperation> RequireOperation(Guid operationId, CancellationToken ct) => client.SendServiceRequest<CountryOperation>(
        "configuration",
        HttpMethod.Get,
        "/country-operations/operations/" + operationId,
        ct: ct);
}
