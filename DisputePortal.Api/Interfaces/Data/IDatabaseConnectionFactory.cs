using System.Data.Common;
using DisputePortal.Api.Repository.Data;

namespace DisputePortal.Api.Interfaces.Data;

public interface IDatabaseConnectionFactory
{
    DatabaseProvider Provider { get; }

    Task<DbConnection> OpenConnection(CancellationToken cancellationToken);

    Task EnsureDatabaseExists(CancellationToken cancellationToken);
}
