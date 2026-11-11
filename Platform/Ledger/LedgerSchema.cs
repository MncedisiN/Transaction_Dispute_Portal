using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.Ledger;

internal static class LedgerSchema
{
    internal static readonly string Sql = SqlScripts.ReadSqlScript("PostgreSql/Schema/Ledger/LedgerSchema/CreateLedgerServiceSchema");
}
