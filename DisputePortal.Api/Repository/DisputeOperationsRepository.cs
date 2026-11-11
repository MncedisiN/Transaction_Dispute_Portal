using Dapper;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Models.Domain;
using System.Data.Common;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Services;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;

public partial class DisputeOperationsRepository(
    IDatabaseConnectionFactory factory,
    IDatabaseDialect databaseDialect,
    CaseServicePolicy servicePolicy,
    IRealtimePublisher? realtime = null) : RepositoryBase(factory, databaseDialect), IOperationsRepository
{
    public async Task UpdateCaseDeadlines(Guid actor, Guid id, UpdateCaseDeadlinesRequest request, CancellationToken ct)
    {
        if (request.ExpectedVersion < 1 || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 5 or > 500)
        {
            throw new PortalValidationException("Add a reason between 5 and 500 characters for changing the targets.");
        }

        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        await RequireSupervisor(c, tx, actor, ct);
        var row = await c.QuerySingleOrDefaultAsync<CaseOperationsDto>(new CommandDefinition(
            SqlScripts.RenderSqlScript("Shared/Queries/Api/DisputeOperationsRepository/GetCaseForTriage", ("CaseSelect", CaseSelect)),
            new { Id = id },
            tx,
            cancellationToken: ct)) ?? throw new PortalNotFoundException();
        if (row.Version != request.ExpectedVersion)
        {
            throw new PortalConflictException("This case changed. Refresh before changing its targets.");
        }

        if (CaseServicePolicy.IsClosed(row.Status))
        {
            throw new PortalConflictException("Closed case targets cannot be changed.");
        }

        var now = DateTimeOffset.UtcNow;
        var response = request.ResponseDueAt.ToUniversalTime();
        var due = request.DueAt.ToUniversalTime();
        var validation = CaseDeadlineRules.ValidateCaseDeadlines(row.CreatedAt, row.FirstResponseAt, row.ResponseDueAt, response, due, now);
        if (validation is not null)
        {
            throw new PortalValidationException(validation);
        }

        if (row.ResponseDueAt == response && row.DueAt == due)
        {
            throw new PortalConflictException("Choose a different target before saving.");
        }

        var changed = await c.ExecuteAsync(new CommandDefinition(
            """
            UPDATE "disputes"."disputes"
            SET
                "responseDueAt" = @Response,
                "dueAt" = @Due,
                "version" = "disputes"."version" + 1,
                "updatedAt" = @Now
            WHERE "disputes"."publicId" = @Id
            AND "disputes"."version" = @Version
            """,
            new
            {
                Id = id,
                Version = request.ExpectedVersion,
                Response = response,
                Due = due,
                Now = now
            },
            tx,
            cancellationToken: ct));
        if (changed != 1)
        {
            throw new PortalConflictException("This case changed. Refresh before changing its targets.");
        }

        await AuditOperation(
            c,
            tx,
            actor,
            "CaseTargetsChanged",
            id,
            new
            {
                previousResponseDueAt = row.ResponseDueAt,
                previousDueAt = row.DueAt,
                responseDueAt = response,
                dueAt = due,
                reason = request.Reason.Trim()
            },
            ct);
        await tx.CommitAsync(ct);
    }

    public async Task Escalate(Guid actor, Guid dispute, EscalationRequest request, CancellationToken ct)
    {
        if (request.ExpectedVersion < 1
            || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length is < 10 or > 500)
        {
            throw new PortalValidationException("Explain the escalation in 10 to 500 characters.");
        }

        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        await EnsureCanWork(c, tx, actor, dispute, ct);
        var row = await c.QuerySingleAsync<CaseOperationsDto>(new CommandDefinition(
            SqlScripts.RenderSqlScript("Shared/Queries/Api/DisputeOperationsRepository/GetCaseForTriage", ("CaseSelect", CaseSelect)),
            new { Id = dispute },
            tx,
            cancellationToken: ct));
        if (row.Version != request.ExpectedVersion)
        {
            throw new PortalConflictException("Refresh this case before escalating.");
        }

        if (CaseServicePolicy.IsClosed(row.Status))
        {
            throw new PortalConflictException("Closed cases cannot be escalated.");
        }

        var now = DateTimeOffset.UtcNow;
        var target = servicePolicy.Targets(5);
        var response = row.CreatedAt.AddHours(target.Response);
        var due = row.CreatedAt.AddHours(target.Resolution);
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeOperationsRepository/EscalateCaseAndResetDeadlines"),
            new
            {
                Id = dispute,
                Now = now,
                request.Reason,
                Response = row.FirstResponseAt is not null && row.ResponseDueAt is { } recordedResponse ? recordedResponse : row.ResponseDueAt is { } oldResponse && oldResponse < response ? oldResponse : response,
                Due = row.DueAt is { } oldDue && oldDue < due ? oldDue : due
            },
            tx,
            cancellationToken: ct));
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeOperationsRepository/InsertNote"),
            new
            {
                Id = Guid.NewGuid(),
                Notice = Guid.NewGuid(),
                Dispute = dispute,
                Actor = actor
            },
            tx,
            cancellationToken: ct));
        var supervisors = await c.QueryAsync<Guid>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeOperationsRepository/GetActiveUsers"),
            transaction: tx,
            cancellationToken: ct));
        var recipients = supervisors.Append(actor).Concat(row.AssignedTo.HasValue ? new[] { row.AssignedTo.Value } : Array.Empty<Guid>());
        await DisputeNotificationOutbox.EnqueueEvent(
            c,
            tx,
            dispute,
            Guid.NewGuid(),
            "CaseEscalated",
            "Case escalated for senior review",
            "Your dispute is receiving senior review. View the case for updates.",
            recipients,
            ct);
        await AuditOperation(c, tx, actor, "CaseEscalated", dispute, new { request.Reason, request.ExpectedVersion }, ct);
        await tx.CommitAsync(ct);
    }

    public async Task SetAvailability(Guid actor, string availability, CancellationToken ct)
    {
        if (availability is not ("Available" or "Busy" or "Offline"))
        {
            throw new PortalValidationException("Choose Available, Busy or Offline.");
        }

        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        if (!(await Roles(c, tx, actor, ct)).Any(r => CaseServicePolicy.IsSpecialist(r) || CaseServicePolicy.IsSupervisor(r)))
        {
            throw new PortalForbiddenException();
        }

        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeOperationsRepository/InsertStaffCapacity"),
            new { Id = actor, Availability = availability },
            tx,
            cancellationToken: ct));
        await AuditOperation(c, tx, actor, "AvailabilityChanged", actor, new { availability }, ct);
        await DispatchLocked(c, tx, ct);
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<CaseOperationsDto>> GetCases(Guid actor, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        var rows = (await c.QueryAsync<CaseOperationsDto>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/DisputeOperationsRepository/GetCaseWorkQueueOrderedByActiveStatus",
            ("CaseSelect", CaseSelect),
            ("StaffScopeSql", StaffScopeSql)),
            new { ActorId = actor },
            cancellationToken: ct))).AsList();
        await DescribeActions(c, actor, rows, ct);
        return rows;
    }

    public async Task<CaseOperationsDto> GetCase(Guid actor, Guid id, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        var row = await c.QuerySingleOrDefaultAsync<CaseOperationsDto>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/DisputeOperationsRepository/GetDisputeOperationsByIdAndActorId",
            ("CaseSelect", CaseSelect),
            ("StaffScopeSql", StaffScopeSql)),
            new { Id = id, ActorId = actor },
            cancellationToken: ct)) ?? throw new PortalNotFoundException();
        await DescribeActions(c, actor, [row], ct);
        return row;
    }

    public async Task ClaimCase(Guid actor, Guid id, ClaimCaseRequest request, CancellationToken ct)
    {
        if (request.ExpectedVersion < 1 || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 5 or > 500)
        {
            throw new PortalValidationException("Add a brief reason for taking this case.");
        }

        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        var row = await c.QuerySingleOrDefaultAsync<CaseOperationsDto>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/DisputeOperationsRepository/GetEligibleCaseForClaim",
            ("CaseSelect", CaseSelect),
            ("StaffScopeSql", StaffScopeSql)),
            new { Id = id, ActorId = actor },
            tx,
            cancellationToken: ct)) ?? throw new PortalNotFoundException();
        var roles = await Roles(c, tx, actor, ct);
        var supervisor = roles.Any(CaseServicePolicy.IsSupervisor);
        if (!supervisor && !roles.Any(role => CaseServicePolicy.CanWork(role, row.QueueType)))
        {
            throw new PortalForbiddenException();
        }

        if (CaseServicePolicy.IsClosed(row.Status))
        {
            throw new PortalConflictException("This case is closed.");
        }

        if (row.AssignedTo == actor)
        {
            await tx.CommitAsync(ct);
            return;
        }

        if (row.Version != request.ExpectedVersion)
        {
            throw new PortalConflictException("This case changed. Refresh before taking it.");
        }

        if (row.AssignedTo != null && !supervisor)
        {
            throw new PortalConflictException("This case already has a specialist. Ask a supervisor to reassign it.");
        }

        if (!supervisor)
        {
            var capacity = await c.QueryFirstOrDefaultAsync<StaffUserDto>(new CommandDefinition(
                SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/DisputeOperationsRepository/GetStaffCapacityForCaseClaim",
                ("StaffSelect", StaffSelect)),
                new { Id = actor },
                tx,
                cancellationToken: ct));
            if (capacity is null || capacity.Status != "Active" || capacity.ActiveCases >= capacity.Capacity)
            {
                throw new PortalConflictException("Your active-case capacity is full. Finish a case or ask a supervisor to review capacity.");
            }
        }

        await SetAssignment(c, tx, row, actor, actor, request.Reason.Trim(), ct);
        await tx.CommitAsync(ct);
    }

    public async Task Assign(Guid actor, Guid id, AssignmentRequest request, CancellationToken ct)
    {
        if (request.ExpectedVersion < 1 || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 5 or > 500)
        {
            throw new PortalValidationException("Add an assignment reason between 5 and 500 characters.");
        }

        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        await RequireSupervisor(c, tx, actor, ct);
        var row = await c.QuerySingleOrDefaultAsync<CaseOperationsDto>(new CommandDefinition(
            SqlScripts.RenderSqlScript("Shared/Queries/Api/DisputeOperationsRepository/GetCaseForTriage", ("CaseSelect", CaseSelect)),
            new { Id = id },
            tx,
            cancellationToken: ct)) ?? throw new PortalNotFoundException();
        if (row.Version != request.ExpectedVersion)
        {
            throw new PortalConflictException("This case changed. Refresh before assigning it.");
        }

        if (CaseServicePolicy.IsClosed(row.Status))
        {
            throw new PortalConflictException("Closed cases cannot be assigned.");
        }

        if (request.UserId is { } selected)
        { 
            var roles = (await c.QueryAsync<StaffUserDto>(new CommandDefinition(
                SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/DisputeOperationsRepository/GetStaffCapacityForCaseClaim",
                ("StaffSelect", StaffSelect)),
                new { Id = selected },
                tx,
                cancellationToken: ct))).AsList();
            var agent = roles.FirstOrDefault(person => CaseServicePolicy.CanBeAssigned(
                person.Role,
                row.QueueType,
                row.EscalationLevel,
                selected == actor));
            if (agent is null || agent.Status != "Active")
            {
                throw new PortalConflictException("Choose active staff qualified for this team, or a senior reviewer for an escalated case.");
            }

            if (selected != actor && agent.Availability != "Available")
            {
                throw new PortalConflictException("This staff member is not available. They can update their work status in the review queue.");
            }

            if (selected != actor && agent.ActiveCases >= agent.Capacity && row.AssignedTo != selected)
            {
                throw new PortalConflictException("This staff member has reached their case capacity. Choose another qualified reviewer.");
            }
        }

        if (request.UserId == row.AssignedTo && (request.UserId is not null || row.AutoAssignmentPaused))
        {
            await tx.CommitAsync(ct);
            return;
        }

        await SetAssignment(c, tx, row, request.UserId, actor, request.Reason.Trim(), ct);
        await tx.CommitAsync(ct);
    }

    private async Task SetAssignment(
        DbConnection c,
        DbTransaction tx,
        CaseOperationsDto row,
        Guid? selected,
        Guid? actor,
        string reason,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeOperationsRepository/UpdateDisputesWithLastAssignedAt"),
            new
            {
                Selected = selected,
                Paused = selected is null,
                Now = now,
                row.Id
            },
            tx,
            cancellationToken: ct));
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeOperationsRepository/UpdateConversations"),
            new
            {
                Selected = selected,
                Now = now,
                row.Id,
                row.QueueType,
                row.EscalationLevel
            },
            tx,
            cancellationToken: ct));
        await AuditOperation(
            c,
            tx,
            actor,
            selected is null ? "CaseUnassigned" : "CaseAssigned",
            row.Id,
            new
            {
                previous = row.AssignedTo,
                assignedTo = selected,
                assignedName = await c.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeOperationsRepository/GetCustomerProfile"),
                new { Id = selected },
                tx,
                cancellationToken: ct)),
                reason
            },
            ct);
        var recipients = new[]
        {
            row.AssignedTo,
            selected
        }.Where(person => person.HasValue).Select(person => person!.Value).Distinct();
        await DisputeNotificationOutbox.EnqueueEvent(
            c,
            tx,
            row.Id,
            Guid.NewGuid(),
            selected is null ? "CaseUnassigned" : "CaseAssigned",
            selected is null ? "Case awaiting assignment" : "Case assigned",
            selected is null ? "Your case has been unassigned and is awaiting a new specialist." : "Your case has been assigned to a qualified specialist.",
            recipients,
            ct);
    }

    public async Task Triage(Guid actor, Guid id, TriageRequest request, CancellationToken ct)
    {
        if (request.ExpectedVersion < 1
            || request.Priority is < 1 or > 5
            || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length is < 5 or > 500)
        {
            throw new PortalValidationException("Choose a priority and add a reason between 5 and 500 characters.");
        }

        if (request.Severity is not ("Low" or "Medium" or "High" or "Critical")
            || !CaseServicePolicy.Queues.Contains(request.QueueType))
        {
            throw new PortalValidationException("Choose a valid severity and team.");
        }

        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        await RequireSupervisor(c, tx, actor, ct);
        var row = await c.QuerySingleOrDefaultAsync<CaseOperationsDto>(new CommandDefinition(
            SqlScripts.RenderSqlScript("Shared/Queries/Api/DisputeOperationsRepository/GetCaseForTriage", ("CaseSelect", CaseSelect)),
            new { Id = id },
            tx,
            cancellationToken: ct)) ?? throw new PortalNotFoundException();
        if (row.Version != request.ExpectedVersion)
        {
            throw new PortalConflictException("This case changed. Refresh before updating triage.");
        }

        if (CaseServicePolicy.IsClosed(row.Status))
        {
            throw new PortalConflictException("Closed case targets cannot be changed.");
        }

        if (row.QueueType != request.QueueType && row.AssignedTo is { } previousAssignee)
        {
            await AuditOperation(
                c,
                tx,
                actor,
                "CaseUnassigned",
                id,
                new
                {
                    previous = previousAssignee,
                    reason = request.Reason.Trim(),
                    queue = request.QueueType
                },
                ct);
            await DisputeNotificationOutbox.EnqueueEvent(
                c,
                tx,
                id,
                Guid.NewGuid(),
                "CaseUnassigned",
                "Case team changed",
                "Your case was moved to the appropriate team and is awaiting assignment.",
                [previousAssignee],
                ct);
        }

        var targets = servicePolicy.Targets(request.Priority);
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeOperationsRepository/UpdateCaseRoutingAssignmentAndDeadlines"),
            new
            {
                request.Priority,
                request.Severity,
                request.QueueType,
                Response = row.FirstResponseAt is null ? row.CreatedAt.AddHours(targets.Response) : row.ResponseDueAt ?? row.CreatedAt.AddHours(targets.Response),
                Due = row.CreatedAt.AddHours(targets.Resolution),
                Assigned = row.QueueType == request.QueueType ? row.AssignedTo : null,
                AssignedAt = row.QueueType == request.QueueType ? row.AssignedAt : null,
                Now = DateTimeOffset.UtcNow,
                Id = id
            },
            tx,
            cancellationToken: ct));
        await AuditOperation(
            c,
            tx,
            actor,
            "CaseTriaged",
            id,
            new
            {
                before = new
                {
                    row.Priority,
                    row.Severity,
                    row.QueueType,
                    row.AssignedTo,
                    row.DueAt
                },
                request.Priority,
                request.Severity,
                request.QueueType,
                request.Reason
            },
            ct);
        await DispatchLocked(c, tx, ct);
        await tx.CommitAsync(ct);
    }
     
    internal async Task<CaseClassification> InitializeCase(DbConnection c, DbTransaction tx, Guid id, string category, CancellationToken ct)
    {
        var facts = await c.QuerySingleAsync<IntakeFacts>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeOperationsRepository/GetDispute"),
            new { Id = id },
            tx,
            cancellationToken: ct));
        var classification = servicePolicy.ClassifyCaseReviewQueue(category, facts.TransactionType, facts.Amount, facts.Currency);
        var targets = servicePolicy.Targets(classification.Priority);
        await c.ExecuteAsync(new CommandDefinition(
            """
            UPDATE "disputes"."disputes"
            SET
                "priority" = @Priority,
                "queueType" = @QueueType,
                "severity" = @Severity,
                "responseDueAt" = @Response,
                "dueAt" = @Due
            WHERE "disputes"."publicId" = @Id
            """,
            new
            {
                Id = id,
                classification.Priority,
                classification.QueueType,
                classification.Severity,
                Response = facts.CreatedAt.AddHours(targets.Response),
                Due = facts.CreatedAt.AddHours(targets.Resolution)
            },
            tx,
            cancellationToken: ct));
        await AuditOperation(
            c,
            tx,
            facts.CustomerId,
            "CaseWorkflowStarted",
            id,
            new
            {
                Category = category,
                classification.TransactionType,
                classification.QueueType,
                classification.Severity,
                classification.Priority,
                Status = "Submitted"
            },
            ct);
        return classification;
    }

    public async Task AssignWaitingCases(CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        var missing = (await c.QueryAsync<CaseOperationsDto>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/DisputeOperationsRepository/GetCasesMissingRoutingDeadlines",
            ("CaseSelect", CaseSelect)),
            transaction: tx,
            cancellationToken: ct))).AsList();
        foreach (var row in missing)
        {
            var target = servicePolicy.Targets(row.Priority);
            await c.ExecuteAsync(new CommandDefinition(
                """
                UPDATE "disputes"."disputes"
                SET
                    "responseDueAt" = @Response,
                    "dueAt" = @Due
                WHERE "disputes"."publicId" = @Id
                """,
                new
                {
                    row.Id,
                    Response = row.CreatedAt.AddHours(target.Response),
                    Due = row.CreatedAt.AddHours(target.Resolution)
                },
                tx,
                cancellationToken: ct));
        }

        var assigned = await DispatchLocked(c, tx, ct);
        var ids = missing.Select(row => row.Id).Concat(assigned).Distinct().ToArray();
        IReadOnlyList<RealtimeCase> changed = realtime is not null && ids.Length > 0 ? (await c.QueryAsync<RealtimeCase>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/DisputeOperationsRepository/GetMatchingCaseAndCustomerIds",
            ("MembershipPredicate", dialect.InListExpression("\"publicId\"", "@Ids"))),
            new { Ids = ids },
            tx,
            cancellationToken: ct))).AsList() : [];
        await tx.CommitAsync(ct);
        foreach (var customer in changed.GroupBy(row => row.CustomerId))
        {
            await realtime!.PublishPortalUpdate(
                [customer.Key],
                false,
                ["disputes", "notifications"],
                customer.Count() == 1 ? customer.First().Id : null,
                ct);
        }

        if (changed.Count > 0)
        {
            await realtime!.PublishPortalUpdate([], true, ["disputes", "notifications"], null, ct);
        }
    }

    internal async Task<IReadOnlyList<Guid>> DispatchLocked(DbConnection c, DbTransaction tx, CancellationToken ct)
    {
        var changed = new List<Guid>();
        var agents = (await c.QueryAsync<StaffUserDto>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/DisputeOperationsRepository/GetAvailableSpecialistsForCaseAssignment",
            ("StaffSelect", StaffSelect)),
            transaction: tx,
            cancellationToken: ct))).AsList();
        foreach (var queue in CaseServicePolicy.Queues)
        {
            var cases = await c.QueryAsync<CaseOperationsDto>(new CommandDefinition(
                SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/DisputeOperationsRepository/GetDisputeOperationsByQueue",
                ("CaseSelect", CaseSelect),
                ("FalseValue", dialect.FalseLiteral)),
                new { Queue = queue },
                transaction: tx,
                cancellationToken: ct));
            foreach (var row in cases)
            {
                var agent = agents.Where(a => CaseServicePolicy.CanWork(a.Role, row.QueueType) && a.ActiveCases < a.Capacity).OrderBy(a => a.ActiveCases).ThenBy(a => a.LastAssignedAt ?? DateTimeOffset.MinValue).ThenBy(a => a.Id).FirstOrDefault();
                if (agent is null)
                {
                    continue;
                }

                await SetAssignment(c, tx, row, agent.Id, null, "Automatic: priority, due date, available capacity and fair rotation.", ct);
                changed.Add(row.Id);
                agent.ActiveCases++;
                agent.LastAssignedAt = DateTimeOffset.UtcNow;
            }
        }

        return changed;
    }

    private class IntakeFacts
    {
        public string TransactionType { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public Guid CustomerId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }

        public IntakeFacts()
        {
        }

        public IntakeFacts(string TransactionType, decimal Amount, string Currency, Guid CustomerId, DateTimeOffset CreatedAt)
        {
            this.TransactionType = TransactionType;
            this.Amount = Amount;
            this.Currency = Currency;
            this.CustomerId = CustomerId;
            this.CreatedAt = CreatedAt;
        }
    }

    private class RealtimeCase
    {
        public Guid Id { get; set; }
        public Guid CustomerId { get; set; }

        public RealtimeCase()
        {
        }

        public RealtimeCase(Guid Id, Guid CustomerId)
        {
            this.Id = Id;
            this.CustomerId = CustomerId;
        }
    }
}
