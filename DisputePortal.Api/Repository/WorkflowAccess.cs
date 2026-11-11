using DisputePortal.Api.Models.Domain;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;
// Actor expressions are trusted SQL fragments from repository code, never customer input.
// Public identifiers are resolved through identity.users; all relationships use numeric FKs.
internal static class WorkflowAccess
{
    internal static string Permission(string actor, string permission) => SqlScripts.RenderSqlScript(
        "Shared/Queries/Api/WorkflowAccess/BuildActorPermissionPredicate",
        ("Actor", actor),
        ("PermissionPredicate", permission));

    internal static string Supervisor(string actor) => SqlScripts.RenderSqlScript(
        "Shared/Queries/Api/WorkflowAccess/BuildSupervisorPermissionPredicate",
        ("PermissionPredicate", Permission(actor, "operations.manage")),
        ("Actor", actor));

    private static string Team(string actor, string queueColumn)
    {
        var responsibilities = string.Join(
            " OR ",
            CaseServicePolicy.Queues.Select(queue => $"({queueColumn} = '{queue}' AND team_role.code = '{CaseServicePolicy.SpecialistFor(queue)}')"));
        return SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/WorkflowAccess/BuildActorTeamMembershipPredicate",
            ("Actor", actor),
            ("Responsibilities", responsibilities));
    }

    internal static string CaseReadScope(string actor = "@ActorId") => SqlScripts.RenderSqlScript(
        "Shared/Queries/Api/WorkflowAccess/BuildCaseReadScopePredicate",
        ("PermissionPredicate", Permission(actor, "disputes.read")),
        ("Actor", actor),
        ("Supervisor", Supervisor(actor)),
        ("RelatedActor", actor),
        ("Team", Team(actor, "d.\"queueType\"")));

    internal static string ReadScope(string actor = "@ActorId") => SqlScripts.RenderSqlScript(
        "Shared/Queries/Api/WorkflowAccess/BuildConversationReadScopePredicate",
        ("Actor", actor),
        ("RelatedActor", actor),
        ("PermissionPredicate", Permission(actor, "disputes.read")),
        ("Supervisor", Supervisor(actor)),
        ("AdditionalActor", actor),
        ("Team", Team(actor, "st.\"queueType\"")),
        ("CaseReadScope", CaseReadScope(actor)),
        ("RelatedSupervisor", Supervisor(actor)),
        ("RelatedTeam", Team(actor, "linked_case.\"queueType\"")));
}
