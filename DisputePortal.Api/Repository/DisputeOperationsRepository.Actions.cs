using Dapper;
using DisputePortal.Api.Service.Disputes;
using System.Data.Common;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;

public partial class DisputeOperationsRepository
{
    internal async Task DescribeActions(DbConnection connection, Guid actor, IReadOnlyList<CaseOperationsDto> cases, CancellationToken ct)
    {
        if (cases.Count == 0)
        {
            return;
        }

        var roles = await Roles(connection, null, actor, ct);
        var permissions = (await connection.QueryAsync<string>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeOperationsRepository/GetActivePermissionsForActor"),
            new { Actor = actor },
            cancellationToken: ct))).ToHashSet(StringComparer.Ordinal);
        var roster = permissions.Contains("disputes.manage") ? (await connection.QueryAsync<StaffUserDto>(new CommandDefinition(
            StaffSelect,
            cancellationToken: ct))).AsList() : [];
        var closedIds = cases.Where(item => CaseServicePolicy.IsClosed(item.Status)).Select(item => item.Id).ToArray();
        var blockedReopening = new HashSet<Guid>();
        if (closedIds.Length > 0 && roles.Contains("super_admin"))
        {
            blockedReopening = (await connection.QueryAsync<Guid>(new CommandDefinition(
                SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/DisputeOperationsRepository/GetDisputePublicId",
                ("MembershipPredicate", dialect.InListExpression("current_case.\"publicId\"", "@Ids"))),
                new { Ids = closedIds },
                cancellationToken: ct))).ToHashSet();
        }

        foreach (var item in cases)
            item.Actions = CaseActionPresenter.Describe(item, actor, roles, permissions, roster, blockedReopening.Contains(item.Id));
    }
}
