using System.Data.Common;
using System.Globalization;
using Dapper;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.ExternalServices;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;
// One set query enriches a notification page; the original financial records remain unchanged.
internal static class RefundRecallNotification
{
    internal static async Task<Dictionary<Guid, string>> GetRefundRecallNotificationDetails(
        DbConnection connection,
        DbTransaction? transaction,
        IDatabaseDialect dialect,
        IEnumerable<Guid> disputes,
        IMessageTemplates templates,
        CancellationToken ct)
    {
        var ids = disputes.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var rows = await connection.QueryAsync<Context>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/RefundRecallNotification/GetRefundRecalls",
            ("MembershipPredicate", dialect.InListExpression("d.\"publicId\"", "@Ids"))),
            new { Ids = ids },
            transaction,
            cancellationToken: ct));
        return rows.ToDictionary(
            row => row.DisputeId,
            row => templates.Render(
                "workflow/RefundRecalled",
                new Dictionary<string, string>
                {
                    ["amount"] = row.Amount.ToString("0.00", CultureInfo.InvariantCulture) + " " + row.Currency,
                    ["account"] = row.AccountType + " ••••" + row.AccountNumber[^Math.Min(4, row.AccountNumber.Length)..],
                    ["reference"] = row.Reference
                }).Text);
    }

    private sealed class Context
    {
        public Guid DisputeId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "";
        public string AccountType { get; set; } = "";
        public string AccountNumber { get; set; } = "";
        public string Reference { get; set; } = "";
    }
}
