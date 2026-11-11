using Dapper;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Reporting.Interfaces;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.Reporting;

public class ReportingStore(ServiceDatabase db) : IReportingStore
{
    public static readonly string Schema = SqlScripts.ReadSqlScript("PostgreSql/Schema/Reporting/ReportingStore/CreateReportingSchema");

    public Task Project(DomainEvent e, CancellationToken ct) => db.ProcessEventWithInboxDeduplication(
        e,
        async (c, tx) =>
    {
        if (e.EventType == EventTypes.LedgerTransactionPosted)
        {
            var journal = e.DeserializePayload<LedgerPostedEvent>();
            if (TransactionIds.FindDuplicates(journal.Balances.Select(b => b.AccountId)).Count > 0)
            {
                throw new InvalidOperationException("Duplicate ledger positions.");
            }

            await c.ExecuteAsync(new CommandDefinition(
                    SqlScripts.ReadSqlScript("PostgreSql/Queries/Reporting/ReportingStore/InsertJournal"),
                    new
                    {
                        journal.TransactionId,
                        journal.JournalId,
                        journal.Kind,
                        At = e.Timestamp
                    },
                    tx,
                    cancellationToken: ct));
            foreach (var b in journal.Balances)
                await c.ExecuteAsync(new CommandDefinition(
                    SqlScripts.ReadSqlScript("PostgreSql/Queries/Reporting/ReportingStore/UpsertPosition"),
                    b,
                    tx,
                    cancellationToken: ct));
        }
        else
        {
            var p = e.DeserializePayload<PaymentEvent>();
            await c.ExecuteAsync(new CommandDefinition(
                    SqlScripts.ReadSqlScript("PostgreSql/Queries/Reporting/ReportingStore/InsertPayment"),
                    new
                    {
                        p.PaymentId,
                        p.Currency,
                        p.Amount,
                        p.State,
                        At = e.Timestamp
                    },
                    tx,
                    cancellationToken: ct));
        }
    },
        ct);

    public async Task<object> Summary(CancellationToken ct)
    {
        await using var c = await db.OpenServiceDatabaseConnection(ct);
        return new
        {
            asOf = DateTimeOffset.UtcNow,
            balances = await c.QueryAsync(new CommandDefinition(
            """
            SELECT
                currency,
                SUM(balance) AS balance,
                COUNT(*) AS accounts
            FROM reporting."positions"
            GROUP BY currency
            ORDER BY currency
            """,
            cancellationToken: ct)),
            payments = await c.QueryAsync(new CommandDefinition(
            """
            SELECT
                currency,
                state,
                COUNT(*) AS count,
                SUM(amount) AS volume
            FROM reporting."payments"
            GROUP BY currency, state
            ORDER BY currency, state
            """,
            cancellationToken: ct)),
            journals = await c.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT
                COUNT(*)
            FROM reporting."journals"
            """,
            cancellationToken: ct))
        };
    }
}
