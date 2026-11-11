using System.Data.Common;
using DisputePortal.Platform.Contracts;

namespace DisputePortal.Api.Interfaces.Finance;

public interface ICaseFinancialProjection
{
    Task ApplyFinancialOutcome(DbConnection c, DbTransaction tx, DomainEvent value, CancellationToken ct);
}
