using System.Data.Common;
using Dapper;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;

public partial class SupportRepository
{
    private string SupportCandidateSql => SqlScripts.RenderSqlScript(
        "Shared/Queries/Api/SupportRepository/GetAvailableSupportStaffWithWorkload",
        ("PermissionPredicate", WorkflowAccess.Permission("u.\"publicId\"", "disputes.manage")));

    public async Task<IReadOnlyList<SupportConcernDto>> GetConcerns(CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        return (await c.QueryAsync<SupportConcernDto>(new CommandDefinition(
            SqlScripts.RenderSqlScript("Shared/Queries/Api/SupportRepository/GetConcerns", ("TrueValue", dialect.TrueLiteral)),
            cancellationToken: ct))).AsList();
    }

    private async Task<CapabilityRow> Capabilities(DbConnection c, DbTransaction? tx, Guid actor, CancellationToken ct) =>
        await c.QuerySingleAsync<CapabilityRow>(new CommandDefinition(
        SqlScripts.RenderSqlScript(
        "Shared/Queries/Api/SupportRepository/SelectSupportColumnsWithSupervisorAndReply",
        ("Supervisor", WorkflowAccess.Supervisor("@ActorId")),
        ("PermissionPredicate", WorkflowAccess.Permission("@ActorId", "disputes.manage"))),
        new { ActorId = actor },
        tx,
        cancellationToken: ct));

    private async Task Decorate(DbConnection c, DbTransaction? tx, Guid actor, IEnumerable<SupportThreadDto> rows, CancellationToken ct)
    {
        var rights = await Capabilities(c, tx, actor, ct);
        foreach (var row in rows)
        {
            var owner = row.CustomerId == actor;
            var open = row.Status == "Open";
            var caseOpen = row.DisputeId is null || DisputeWorkflow.CanSendCaseMessage(row.CaseStatus);
            row.CanReply = open && caseOpen && (owner || rights.Reply == 1 && (rights.Supervisor == 1 || row.AssignedTo == actor));
            row.CanClose = open
                && (!row.DisputeId.HasValue
                || !caseOpen)
                && (owner
                || rights.Reply == 1
                && (rights.Supervisor == 1
                || row.AssignedTo == actor));
            row.CanAssign = open && !owner && rights.Supervisor == 1;
            row.CanEscalate = open && !owner && row.CanReply && row.EscalationLevel < 3;
            if (owner)
            {
                row.QueueType = null;
                row.AssignedTo = null;
                row.EscalationLevel = 0;
            }
        }
    }

    private static void EnsureSupportOpen(string status)
    {
        if (status != "Open")
        {
            throw new PortalConflictException("This conversation is permanently closed. Start a new enquiry for further assistance.");
        }
    }

    private async Task<ConcernRow> GetConversationConcern(
        DbConnection c,
        DbTransaction tx,
        SupportThreadDto thread,
        string? concernCode,
        CancellationToken ct)
    {
        var code = concernCode ?? (thread.DisputeId.HasValue
            || thread.TransactionId.HasValue ? "Dispute" : thread.RelatedDisputeId.HasValue ? "FollowUp" : "General");
        var concern = await c.QuerySingleOrDefaultAsync<ConcernRow>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/SupportRepository/GetEnabledConcernRoutingPolicy",
            ("TrueValue", dialect.TrueLiteral)),
            new { Code = code },
            tx,
            cancellationToken: ct)) ?? throw new PortalValidationException("Choose an available concern type.");
        if (concern.RequiresCase && thread.DisputeId is null && thread.RelatedDisputeId is null)
        {
            throw new PortalValidationException("Select the related dispute for this concern.");
        }

        return concern;
    }

    private async Task PrepareConversation(
        DbConnection c,
        DbTransaction tx,
        SupportThreadDto thread,
        ConcernRow concern,
        CancellationToken ct)
    {
        var code = concern.Code;
        var queue = concern.QueueType;
        var escalation = 0;
        if ((thread.DisputeId ?? thread.RelatedDisputeId) is { } linkedCase)
        {
            var routing = await c.QuerySingleAsync<ConversationRouting>(new CommandDefinition(
                """
                SELECT
                    "transaction_disputes"."queueType" AS QueueType,
                    "transaction_disputes"."escalationLevel" AS EscalationLevel
                FROM "disputes"."disputes" AS "transaction_disputes"
                WHERE "transaction_disputes"."publicId" = @Id
                """,
                new { Id = linkedCase },
                tx,
                cancellationToken: ct));
            queue = routing.QueueType;
            escalation = routing.EscalationLevel;
        }
        else if (thread.TransactionId is { } transaction && code == "Dispute")
        {
            var type = await c.ExecuteScalarAsync<string>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/SupportRepository/GetTransaction"),
                new { Id = transaction },
                tx,
                cancellationToken: ct));
            queue = CaseServicePolicy.Route("Other", type ?? "");
        }

        await c.ExecuteAsync(new CommandDefinition(
            """
            UPDATE "support"."conversations"
            SET
                "concernId" = @Concern,
                "queueType" = @Queue,
                "escalationLevel" = @Escalation,
                "priority" = @Priority
            WHERE "conversations"."publicId" = @Id
            """,
            new
            {
                thread.Id,
                Concern = concern.Id,
                Queue = queue,
                Escalation = escalation,
                concern.Priority
            },
            tx,
            cancellationToken: ct));
        thread.ConcernCode = concern.Code;
        thread.ConcernLabel = concern.Label;
        thread.QueueType = queue;
        thread.EscalationLevel = escalation;
        if (thread.DisputeId is { } caseId)
        {
            var inherited = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/SupportRepository/GetActiveDispute",
                ("PermissionPredicate", WorkflowAccess.Permission("u.\"publicId\"", "disputes.manage")),
                ("CaseReadScope", WorkflowAccess.CaseReadScope("u.\"publicId\""))),
                new { Id = caseId },
                tx,
                cancellationToken: ct));
            if (inherited.HasValue)
            {
                await SetSupportAssignment(c, tx, thread, inherited, null, "Inherited the eligible financial case owner.", ct);
                return;
            }
        }

        if (escalation > 0)
        {
            await DisputeNotificationOutbox.EnqueueEvent(
                c,
                tx,
                null,
                thread.Id,
                "SupportQueued",
                "Support Enquiry Queued",
                "A support enquiry requires authorized senior review.",
                await Supervisors(c, tx, null, ct),
                ct,
                "/support?thread=" + thread.Id,
                thread.Id);
            return;
        }

        await AssignNext(c, tx, thread, ct);
    }

    private async Task<IReadOnlyList<Guid>> Supervisors(DbConnection c, DbTransaction tx, Guid? exclude, CancellationToken ct) =>
        (await c.QueryAsync<Guid>(new CommandDefinition(
        SqlScripts.RenderSqlScript(
        "Shared/Queries/Api/SupportRepository/GetActiveUsers",
        ("Supervisor", WorkflowAccess.Supervisor("u.\"publicId\""))),
        new { Exclude = exclude },
        tx,
        cancellationToken: ct))).AsList();

    private async Task AssignNext(DbConnection c, DbTransaction tx, SupportThreadDto thread, CancellationToken ct)
    {
        if (thread.Status != "Open" || thread.AssignedTo.HasValue || thread.EscalationLevel > 0)
        {
            return;
        }

        var candidates = (await c.QueryAsync<SupportCandidate>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/SupportRepository/FilterSupportStaffByRole",
            ("SupportCandidateSql", SupportCandidateSql)),
            new { Role = CaseServicePolicy.SpecialistFor(thread.QueueType ?? "General") },
            tx,
            cancellationToken: ct))).Where(candidate => candidate.Workload < candidate.Capacity).OrderBy(candidate => candidate.Workload).ThenBy(candidate => candidate.LastAssignedAt ?? DateTimeOffset.MinValue).ThenBy(candidate => candidate.Id).ToArray();
        var selected = candidates.FirstOrDefault();
        if (selected is null)
        {
            await DisputeNotificationOutbox.EnqueueEvent(
                c,
                tx,
                null,
                thread.Id,
                "SupportQueued",
                "Support Enquiry Queued",
                "A support enquiry needs an eligible specialist.",
                await Supervisors(c, tx, null, ct),
                ct,
                "/support?thread=" + thread.Id,
                thread.Id);
            return;
        }

        await SetSupportAssignment(
            c,
            tx,
            thread,
            selected.Id,
            null,
            "Automatic: eligible team, available capacity and least active workload.",
            ct);
    }

    private async Task SetSupportAssignment(
        DbConnection c,
        DbTransaction tx,
        SupportThreadDto thread,
        Guid? selected,
        Guid? actor,
        string reason,
        CancellationToken ct)
    {
        var changed = await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/SupportRepository/UpdateConversationsWithAssignedToId"),
            new
            {
                thread.Id,
                thread.Version,
                Selected = selected,
                Now = DateTimeOffset.UtcNow
            },
            tx,
            cancellationToken: ct));
        if (changed != 1)
        {
            throw new PortalConflictException("This conversation changed. Reload before assigning it.");
        }

        if (selected.HasValue)
        {
            await c.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/SupportRepository/UpdateStaffCapacityForUser"),
                new { User = selected, Now = DateTimeOffset.UtcNow },
                tx,
                cancellationToken: ct));
        }

        await AuditOperation(
            c,
            tx,
            actor,
            "SupportAssigned",
            thread.Id,
            new
            {
                previous = thread.AssignedTo,
                assigned = selected,
                thread.QueueType,
                reason
            },
            ct);
        thread.AssignedTo = selected;
        thread.Version++;
        if (selected.HasValue)
        {
            await DisputeNotificationOutbox.EnqueueEvent(
                c,
                tx,
                null,
                Guid.NewGuid(),
                "SupportAssigned",
                "Support Enquiry Assigned",
                "A support enquiry has been assigned to you.",
                [selected.Value],
                ct,
                "/support?thread=" + thread.Id,
                thread.Id);
        }
    }

    public async Task AssignWaitingSupportConversations(CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        var waiting = await c.QueryAsync<SupportThreadDto>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/SupportRepository/GetSupportPageForAssignWaitingSupportConversations",
            ("ThreadSelect", ThreadSelect)),
            new { ActorId = Guid.Empty },
            tx,
            cancellationToken: ct));
        foreach (var thread in waiting)
            await AssignNext(c, tx, thread, ct);
        await tx.CommitAsync(ct);
    }

    internal static async Task CloseCaseConversations(
        DbConnection c,
        DbTransaction tx,
        Guid dispute,
        Guid? actor,
        DisputePortal.Api.Interfaces.Data.IDatabaseDialect dialect,
        CancellationToken ct)
    {
        var open = (await c.QueryAsync<Guid>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/SupportRepository/GetOpenConversationsForDispute"),
            new { Case = dispute },
            tx,
            cancellationToken: ct))).AsList();
        foreach (var thread in open)
        {
            var now = DateTimeOffset.UtcNow;
            await c.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/SupportRepository/CloseActiveConversationsForResolvedCase"),
                new
                {
                    Thread = thread,
                    Actor = actor,
                    Now = now
                },
                tx,
                cancellationToken: ct));
            await c.ExecuteAsync(new CommandDefinition(
                SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/SupportRepository/InsertAuditEvent",
                ("AuditMetadataValueExpression", dialect.AuditMetadataValueExpression("@Details"))),
                new
                {
                    Id = Guid.NewGuid(),
                    Actor = actor,
                    Thread = thread,
                    Correlation = Guid.NewGuid().ToString(),
                    Now = now,
                    Details = System.Text.Json.JsonSerializer.Serialize(new { dispute, reason = "Financial case completed" })
                },
                tx,
                cancellationToken: ct));
        }
    }

    private async Task<SupportThreadDto> LockedThread(DbConnection c, DbTransaction tx, Guid actor, Guid thread, CancellationToken ct) =>
        await c.QuerySingleOrDefaultAsync<SupportThreadDto>(new CommandDefinition(
        SqlScripts.RenderSqlScript(
        "Shared/Queries/Api/SupportRepository/GetThreadWithinActorReadScope",
        ("ThreadSelect", ThreadSelect),
        ("ThreadScope", ThreadScope)),
        new { Thread = thread, ActorId = actor },
        tx,
        cancellationToken: ct)) ?? throw new PortalNotFoundException();

    public async Task<SupportThreadDto> CloseConversation(Guid actor, Guid thread, CloseSupportRequest request, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        var item = await LockedThread(c, tx, actor, thread, ct);
        if (item.Status == "Closed")
        {
            await tx.CommitAsync(ct);
            return (await GetSupportThread(actor, thread, ct))!;
        }

        if (item.DisputeId.HasValue && DisputeWorkflow.CanSendCaseMessage(item.CaseStatus))
        {
            throw new PortalConflictException("Case messages stay open until the financial case is complete.");
        }

        var assignedBeforeClose = item.AssignedTo;
        await Decorate(c, tx, actor, [item], ct);
        if (!item.CanClose)
        {
            throw new PortalForbiddenException();
        }

        if (item.Version != request.ExpectedVersion)
        {
            throw new PortalConflictException("This conversation changed. Reload before closing it.");
        }

        if (item.CustomerId != actor && string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new PortalValidationException("Add a reason for closing this conversation.");
        }

        if (request.Reason?.Trim().Length > 500)
        {
            throw new PortalValidationException("Keep the closure reason within 500 characters.");
        }

        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/SupportRepository/CloseOwnedSupportConversation"),
            new
            {
                Thread = thread,
                Actor = actor,
                Version = request.ExpectedVersion,
                Now = DateTimeOffset.UtcNow,
                Reason = request.Reason?.Trim()
            },
            tx,
            cancellationToken: ct));
        await AuditOperation(
            c,
            tx,
            actor,
            "SupportConversationClosed",
            thread,
            new
            {
                reason = request.Reason?.Trim(),
                item.DisputeId,
                financialCaseUnchanged = true
            },
            ct);
        IReadOnlyList<Guid> recipients = item.CustomerId == actor ? (assignedBeforeClose.HasValue ? new[]
        {
            assignedBeforeClose.Value
        }

        : await Supervisors(c, tx, actor, ct)) : new[]
        {
            item.CustomerId
        };
        await DisputeNotificationOutbox.EnqueueEvent(
            c,
            tx,
            null,
            Guid.NewGuid(),
            "SupportClosed",
            "Conversation Closed",
            "Your support conversation is now closed. Its history remains available.",
            recipients,
            ct,
            "/support?thread=" + thread,
            thread);
        await tx.CommitAsync(ct);
        return (await GetSupportThread(actor, thread, ct))!;
    }

    public async Task<SupportThreadDto> AssignConversation(Guid actor, Guid thread, AssignmentRequest request, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        var item = await LockedThread(c, tx, actor, thread, ct);
        EnsureSupportOpen(item.Status);
        if ((await Capabilities(c, tx, actor, ct)).Supervisor != 1)
        {
            throw new PortalForbiddenException();
        }

        if (item.Version != request.ExpectedVersion)
        {
            throw new PortalConflictException("This conversation changed. Reload before assigning it.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 5 or > 500)
        {
            throw new PortalValidationException("Add a concise assignment reason.");
        }

        if (request.UserId is { } selected)
        {
            var target = await c.QuerySingleOrDefaultAsync<SupportCandidate>(new CommandDefinition(
                SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/SupportRepository/FilterSupportStaffByTargetAndRole",
                ("SupportCandidateSql", SupportCandidateSql),
                ("Supervisor", WorkflowAccess.Supervisor("u.\"publicId\""))),
                new { Target = selected, Role = CaseServicePolicy.SpecialistFor(item.QueueType ?? "General") },
                tx,
                cancellationToken: ct));
            if (target is null || target.Workload >= target.Capacity)
            {
                throw new PortalConflictException("Choose an available, qualified staff member with capacity.");
            }

            if (item.EscalationLevel > 0
                && await c.ExecuteScalarAsync<int>(new CommandDefinition(
                SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/SupportRepository/SelectSupportColumnsForAssignConversation",
                ("Supervisor", WorkflowAccess.Supervisor("@ActorId"))),
                new { ActorId = selected },
                tx,
                cancellationToken: ct)) != 1)
            {
                throw new PortalForbiddenException("An escalated conversation requires an authorized supervisor.");
            }
        }

        await SetSupportAssignment(c, tx, item, request.UserId, actor, request.Reason.Trim(), ct);
        await tx.CommitAsync(ct);
        return (await GetSupportThread(actor, thread, ct))!;
    }

    public async Task<SupportThreadDto> EscalateConversation(Guid actor, Guid thread, EscalationRequest request, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        var item = await LockedThread(c, tx, actor, thread, ct);
        await Decorate(c, tx, actor, [item], ct);
        EnsureSupportOpen(item.Status);
        if (!item.CanEscalate)
        {
            throw new PortalForbiddenException();
        }

        if (item.Version != request.ExpectedVersion)
        {
            throw new PortalConflictException("This conversation changed. Reload before escalating it.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 5 or > 500)
        {
            throw new PortalValidationException("Add a concise escalation reason.");
        }

        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/SupportRepository/EscalateConversationAndClearAssignment"),
            new { Id = thread, Version = request.ExpectedVersion },
            tx,
            cancellationToken: ct));
        await AuditOperation(c, tx, actor, "SupportEscalated", thread, new { reason = request.Reason.Trim(), item.QueueType }, ct);
        await DisputeNotificationOutbox.EnqueueEvent(
            c,
            tx,
            null,
            Guid.NewGuid(),
            "SupportEscalated",
            "Support Enquiry Escalated",
            "A support enquiry needs senior review.",
            await Supervisors(c, tx, actor, ct),
            ct,
            "/support?thread=" + thread,
            thread);
        await tx.CommitAsync(ct);
        // Escalation can revoke the previous investigator's access immediately.
        return new SupportThreadDto
        {
            Id = thread,
            Status = "Open",
            Version = request.ExpectedVersion + 1,
            Subject = "Escalated For Senior Review"
        };
    }

    private class ConcernRow
    {
        public long Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string QueueType { get; set; } = string.Empty;
        public int Priority { get; set; }
        public bool RequiresCase { get; set; }

        public ConcernRow()
        {
        }

        public ConcernRow(long Id, string Code, string Label, string QueueType, int Priority, bool RequiresCase)
        {
            this.Id = Id;
            this.Code = Code;
            this.Label = Label;
            this.QueueType = QueueType;
            this.Priority = Priority;
            this.RequiresCase = RequiresCase;
        }
    }

    private class SupportCandidate
    {
        public Guid Id { get; set; }
        public long Workload { get; set; }
        public int Capacity { get; set; }
        public DateTimeOffset? LastAssignedAt { get; set; }
    }

    private class CapabilityRow
    {
        public int Supervisor { get; set; }
        public int Reply { get; set; }

        public CapabilityRow()
        {
        }

        public CapabilityRow(int Supervisor, int Reply)
        {
            this.Supervisor = Supervisor;
            this.Reply = Reply;
        }
    }

    private class ConversationRouting
    {
        public string QueueType { get; set; } = string.Empty;
        public int EscalationLevel { get; set; }

        public ConversationRouting()
        {
        }

        public ConversationRouting(string QueueType, int EscalationLevel)
        {
            this.QueueType = QueueType;
            this.EscalationLevel = EscalationLevel;
        }
    }
}
