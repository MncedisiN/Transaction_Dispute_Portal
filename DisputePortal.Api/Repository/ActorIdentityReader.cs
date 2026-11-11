using System.Data.Common;
using Dapper;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;

internal static class ActorIdentityReader
{
    public static async Task<IReadOnlyDictionary<Guid, ActionActorDto>> GetRecordedActorIdentities(
        DbConnection connection,
        DbTransaction? transaction,
        IDatabaseDialect dialect,
        string source,
        IEnumerable<Guid> ids,
        bool internalDetails,
        CancellationToken ct)
    {
        var keys = ids.Distinct().ToArray();
        if (keys.Length == 0)
        {
            return new Dictionary<Guid, ActionActorDto>();
        }
         
        var parent = source switch
        {
            "Audit" => (Table: "audit.\"auditEvents\"", ForeignKey: "auditEventId"),
            "Automatic" => (Table: "disputes.\"automaticDecisions\"", ForeignKey: "automaticDecisionId"),
            "Evidence" => (Table: "disputes.evidence", ForeignKey: "evidenceId"),
            "History" => (Table: "disputes.\"statusHistory\"", ForeignKey: "historyId"),
            "Message" => (Table: "support.messages", ForeignKey: "messageId"),
            "Note" => (Table: "disputes.notes", ForeignKey: "noteId"),
            "Security" => (Table: "\"identity\".\"securityEvents\"", ForeignKey: "securityEventId"),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/ActorIdentityReader/GetActorSnapshots",
            ("Table", parent.Table),
            ("ForeignKey", parent.ForeignKey),
            ("MembershipPredicate", dialect.InListExpression("recorded_event.\"publicId\"", "@Ids"))),
            new { Source = source, Ids = keys },
            transaction,
            cancellationToken: ct));
        return rows.ToDictionary(
            row => row.EventId,
            row =>
        {
            var roles = (row.RoleKeys ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries);
            var identity = new ActionActorDto(
                    internalDetails ? row.ActorUserId : null,
                    row.FirstName,
                    row.MiddleName,
                    row.LastName,
                    row.AccountName,
                    internalDetails ? roles : [],
                    row.ActorKind,
                    row.Provenance,
                    internalDetails ? row.LoginIdentifier : null); 
            if (internalDetails)
            {
                return identity;
            }

            var fallbackName = roles.Contains("customer") ? row.AccountName : row.ActorKind == "System" ? "System" : "Support team";
            return new ActionActorDto(identity)
            {
                AccountName = identity.FullName ?? fallbackName,
                Provenance = null
            };
        });
    }

    private class Row
    {
        public Guid EventId { get; set; }
        public Guid? ActorUserId { get; set; }
        public string? FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public string? AccountName { get; set; }
        public string? LoginIdentifier { get; set; }
        public string? RoleKeys { get; set; }
        public string ActorKind { get; set; } = string.Empty;
        public string Provenance { get; set; } = string.Empty;

        public Row()
        {
        }

        public Row(
            Guid EventId,
            Guid? ActorUserId,
            string? FirstName,
            string? MiddleName,
            string? LastName,
            string? AccountName,
            string? LoginIdentifier,
            string? RoleKeys,
            string ActorKind,
            string Provenance)
        {
            this.EventId = EventId;
            this.ActorUserId = ActorUserId;
            this.FirstName = FirstName;
            this.MiddleName = MiddleName;
            this.LastName = LastName;
            this.AccountName = AccountName;
            this.LoginIdentifier = LoginIdentifier;
            this.RoleKeys = RoleKeys;
            this.ActorKind = ActorKind;
            this.Provenance = Provenance;
        }
    }
}
