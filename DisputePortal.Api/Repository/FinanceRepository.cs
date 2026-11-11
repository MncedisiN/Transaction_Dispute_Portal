using DisputePortal.Api.Service.ExternalServices;
using DisputePortal.Api.Configuration;
using DisputePortal.Api.Platform;
using DisputePortal.Platform.Contracts;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Dapper;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Repository.Data;
using DisputePortal.Api.Interfaces.Finance;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.ExternalServices;
using DisputePortal.Api.Interfaces.Services;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;
public class FinanceRepository(
    IDatabaseConnectionFactory factory,
    IDatabaseDialect databaseDialect,
    TimeZoneInfo businessTimeZone,
    ApiRuntimeOptions runtime,
    CaseServicePolicy servicePolicy,
    ILogger<FinanceRepository>? logger = null,
    IRealtimePublisher? realtime = null,
    IDisputeFinancialCommands? financialCommands = null,
    IMessageTemplates? templates = null,
    IConfiguration? configuration = null,
    IHostEnvironment? environment = null) : RepositoryBase(factory, databaseDialect), IFinanceRepository, ICaseFinancialProjection
{
    private static readonly string RefundSql = SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/GetDisputeRefundReceipt");
    private static readonly string RecallSql = SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/GetRefundRecalls");

    private static RefundRecallDto MapRecall(RecallRow r, bool replay = false) => new(
        r.Id,
        r.DisputeId,
        r.CreditTransactionId,
        r.ReversalTransactionId,
        r.Amount,
        r.Currency,
        r.PostedAt,
        r.Reason,
        replay);

    private string CaseSql => SqlScripts.RenderSqlScript(
        "Shared/Queries/Api/FinanceRepository/GetCaseFinancialEligibilityFacts",
        ("TrueValue", dialect.TrueLiteral),
        ("RelatedTrueValue", dialect.TrueLiteral));

    private static RefundDto MapRefundApprovalPolicy(RefundRow r, bool replay = false) => new(
        r.DisputeId,
        r.CreditTransactionId,
        r.Amount,
        r.Currency,
        r.PostedAt,
        r.Source == "System",
        replay,
        r.CreditReference,
        r.CustomerReference);

    private async Task RequireCaseRead(DbConnection c, Guid actor, Guid id, CancellationToken ct)
    {
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.RenderSqlScript("Shared/Queries/Api/FinanceRepository/CountDisputesForActor", ("StaffScopeSql", StaffScopeSql)),
            new { Id = id, ActorId = actor },
            cancellationToken: ct)) != 1)
        {
            throw new PortalNotFoundException();
        }
    }

    public async Task<IReadOnlyList<AccountBalanceDto>> GetBalances(Guid customer, CancellationToken ct)
    {
        if (financialCommands is not null)
        {
            return await financialCommands.Balances(customer, ct);
        }

        await using var c = await connections.OpenConnection(ct);
        var rows = await c.QueryAsync<BalanceRow>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/GetCustomerProjectedAccountBalances"),
            new { Customer = customer },
            cancellationToken: ct));
        return rows.Select(x => new AccountBalanceDto(
            x.AccountId,
            x.Currency,
            x.Unknown == 0 ? x.CurrentAmount : null,
            x.Unknown == 0 ? x.CurrentAmount - x.ReservedAmount : null,
            x.Unknown == 0 ? x.ReservedAmount : null,
            x.Unknown == 0,
            x.Status,
            x.AccountType)).ToArray();
    }

    public async Task<BalanceOverviewDto> GetOverview(Guid customer, CancellationToken ct)
    {
        var balances = await GetBalances(customer, ct);
        var included = balances.Where(x => x.Status == "Active" && (x.AccountType == "Cheque" || x.AccountType == "Savings")).DistinctBy(x => x.AccountId).ToArray();
        var known = included.All(x => x.BalanceKnown)
            && included.Select(x => x.Currency).Distinct(StringComparer.Ordinal).Take(2).Count() <= 1;
        return new(
            balances,
            known ? included.Sum(x => x.CurrentBalance ?? 0) : null,
            known ? included.Sum(x => x.AvailableBalance ?? 0) : null,
            included.Length,
            known)
        {
            Currency = known ? included.FirstOrDefault()?.Currency : null
        };
    }

    public async Task<IReadOnlyList<BalanceHistoryDto>> GetBalanceHistory(Guid customer, Guid account, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/CountAccountsForCustomerAndAccount"),
            new { Account = account, Customer = customer },
            cancellationToken: ct)) != 1)
        {
            throw new PortalNotFoundException();
        }

        return (await c.QueryAsync<BalanceHistoryDto>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/GetProjectionEntriesForAccount"),
            new { Account = account },
            cancellationToken: ct))).AsList();
    }

    public async Task<FinancialSummaryDto> GetCaseFinancials(Guid actor, Guid dispute, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await RequireCaseRead(c, actor, dispute, ct);
        var refund = await c.QuerySingleOrDefaultAsync<RefundRow>(new CommandDefinition(
            RefundSql,
            new { Id = dispute },
            cancellationToken: ct));
        var decision = await c.QuerySingleOrDefaultAsync<DecisionRow>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/GetAutomaticDecision"),
            new { Id = dispute },
            cancellationToken: ct));
        var receipt = refund is null ? null : MapRefundApprovalPolicy(refund);
        if (receipt is not null
            && await c.ExecuteScalarAsync<Guid>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/GetDispute"),
            new { Id = dispute },
            cancellationToken: ct)) == actor)
        {
            receipt = new RefundDto(receipt)
            {
                CreditReference = null
            };
        }

        var recall = await c.QuerySingleOrDefaultAsync<RecallRow>(new CommandDefinition(
            RecallSql,
            new { Id = dispute },
            cancellationToken: ct));
        var row = await c.QuerySingleAsync<CaseRow>(new CommandDefinition(CaseSql, new { Id = dispute }, cancellationToken: ct));
        var reinstated = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/CountReinstatements"),
            new { Id = dispute },
            cancellationToken: ct)) > 0;
        var roles = await Roles(c, null, actor, ct);
        var assignment = await c.QuerySingleAsync<CaseOperationsDto>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/FinanceRepository/GetCaseAssignmentForFinancialReview",
            ("CaseSelect", CaseSelect)),
            new { Id = dispute },
            cancellationToken: ct));
        var qualified = roles.Any(CaseServicePolicy.IsSupervisor)
            || (assignment.AssignedTo == actor
            && roles.Any(role => CaseServicePolicy.CanWork(role, assignment.QueueType)));
        var canSettle = qualified && await CanSettleHistorical(row, ct);
        var canRecall = refund is not null
            && recall is null
            && CaseServicePolicy.IsClosed(row.Status)
            && canSettle
            && row.ExternalCount == 0
            && qualified
            && (row.Amount < servicePolicy.HighValueThreshold
            || roles.Any(CaseServicePolicy.IsSupervisor));
        var parent = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/GetOriginalParentDisputeId"),
            new { Id = dispute },
            cancellationToken: ct));
        var child = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/GetReinstatedChildDisputeId"),
            new { Id = dispute },
            cancellationToken: ct));
        var pending = financialCommands is null ? null : await financialCommands.Pending(c, dispute, ct);
        return new(
            receipt,
            decision?.Outcome,
            decision?.RuleCode,
            decision?.PolicyVersion,
            recall is null ? null : MapRecall(recall),
            row.Customer == actor
                && row.ActiveFlag == 1
                && RefundRecallPolicy.CanReinstate(row.Status, recall is not null, reinstated),
            canRecall && pending?.Status != "Processing",
            parent,
            child,
            pending,
            row.Status == "UnderReview"
                && canSettle
                && pending is null
                && await c.ExecuteScalarAsync<int>(new CommandDefinition(
                SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/FinanceRepository/SelectFinanceColumns",
            ("PermissionPredicate", WorkflowAccess.Permission("@ActorId", "refund.approve"))),
                new { ActorId = actor },
                cancellationToken: ct)) == 1);
    }

    public async Task<IReadOnlyList<CaseActivityDto>> GetCaseActivity(Guid actor, Guid dispute, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await RequireCaseRead(c, actor, dispute, ct);
        var internalDetails = await c.ExecuteScalarAsync<Guid>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/GetDispute"),
            new { Id = dispute },
            cancellationToken: ct)) != actor;
        var args = new
        {
            Id = dispute
        };
        var rows = new List<ActivityRow>();
        rows.AddRange(await c.QueryAsync<ActivityRow>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/GetUnderReviewStatusHistory"),
            args,
            cancellationToken: ct)));
        rows.AddRange(await c.QueryAsync<ActivityRow>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/GetNotes"),
            args,
            cancellationToken: ct)));
        rows.AddRange(await c.QueryAsync<ActivityRow>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/GetEvidence"),
            args,
            cancellationToken: ct)));
        rows.AddRange(await c.QueryAsync<ActivityRow>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/GetMessages"),
            args,
            cancellationToken: ct)));
        rows.AddRange(await c.QueryAsync<ActivityRow>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/GetAutomaticDecisions"),
            args,
            cancellationToken: ct)));
        rows.AddRange(await c.QueryAsync<ActivityRow>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/GetCaseEvidenceRemovalAuditEvents"),
            args,
            cancellationToken: ct)));
        var auditActions = internalDetails ? "(a.action LIKE '%Assign%' OR a.action LIKE '%Escalat%' OR a.action IN ('CaseTargetsChanged','CaseTriaged','DisputeRefundApproved','DisputeRefundPosted','DisputeRefundRecalled','DisputeReinstated'))" : SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/FilterFinanceRecords");
        rows.AddRange(await c.QueryAsync<ActivityRow>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/FinanceRepository/GetCaseFinancialAuditEvents",
            ("AuditMetadataSelectExpression", dialect.AuditMetadataSelectExpression("a.\"safeMetadata\"")),
            ("AuditActions", auditActions)),
            args,
            cancellationToken: ct)));
        if (!internalDetails)
        {
            rows.RemoveAll(row => row.Source == "Automatic");
        }

        var identities = new Dictionary<(string Source, Guid Id), ActionActorDto>();
        foreach (var group in rows.GroupBy(row => row.Source))
            foreach (var pair in await ActorIdentityReader.GetRecordedActorIdentities(
            c,
            null,
            dialect,
            group.Key,
            group.Select(row => row.Id),
            internalDetails,
            ct))
                identities[(group.Key, pair.Key)] = pair.Value;
        return rows.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id).Select(x =>
        {
            identities.TryGetValue((x.Source, x.Id), out var identity);
            var detail = x.Detail;
            if (x.Source == "Audit")
            {
                detail = ActivityAuditDetail(x.Action, x.SafeMetadata, internalDetails);
            }

            if (!internalDetails && x.Source == "History")
            {
                detail = null;
            }

            if (!internalDetails && x.Action == "CaseAssigned")
            {
                var assigned = detail?.StartsWith("Assigned to ", StringComparison.Ordinal) == true ? detail[12..].TrimEnd('.') : "Support team";
                return new CaseActivityDto(x.Id.ToString(), "CaseAssigned", assigned, x.OccurredAt, null);
            }

            return new CaseActivityDto(
                x.Id.ToString(),
                x.Action,
                identity?.Label ?? (internalDetails ? "Actor not recorded" : "Support team"),
                x.OccurredAt,
                detail,
                identity);
        }).ToArray();
    }

    private string? ActivityAuditDetail(string action, string? metadata, bool internalDetails)
    {
        try
        {
            using var document = JsonDocument.Parse(metadata ?? "{}");
            var root = document.RootElement;
            string? Text(string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            string Date(string key) => DateTimeOffset.TryParse(Text(key), out var date) ? TimeZoneInfo.ConvertTime(date, businessTimeZone).ToString(
                "dd MMM yyyy h:mm tt",
                System.Globalization.CultureInfo.InvariantCulture) : "Not recorded";
            if (action == "EvidenceRemoved")
            {
                return "File removed. The protected original remains retained.";
            }

            if (action == "CaseAssigned")
            {
                return Text("assignedName") is { Length: > 0 } name ? "Assigned to " + name + "." : "Your case was assigned to a specialist.";
            }

            if (action == "CaseUnassigned")
            {
                return "Your case is awaiting assignment to a new specialist.";
            }

            if (action == "DisputeRefundRecalled")
            {
                return Text("reason");
            }

            if (action == "DisputeReinstated")
            {
                return "Your case was reinstated for further specialist review.";
            }

            if (!internalDetails)
            {
                return null;
            }

            if (action == "CaseTargetsChanged")
            {
                return "Response target: " + Date("previousResponseDueAt") + " to " + Date("responseDueAt") + ". Resolution target: " + Date("previousDueAt") + " to " + Date("dueAt") + ". " + Text("reason");
            }

            return Text("reason") ?? Text("Reason");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<AutomaticRefundPolicyDto> GetPolicy(Guid actor, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await RequireSupervisor(c, null, actor, ct, true);
        return await PolicyVersionRepository.EffectiveAutomatic(c, null, DateTimeOffset.UtcNow, ct) ?? throw new PortalConflictException("No automatic refund policy applies at this time.");
    }

    public Task UpdatePolicy(Guid actor, UpdateAutomaticRefundPolicyRequest request, CancellationToken ct) => new PolicyVersionRepository(
        connections,
        dialect).PublishAutomatic(actor, request, ct);

    public async Task<RefundDto> ApproveRefund(
        Guid actor,
        Guid dispute,
        ApproveRefundRequest request,
        string correlation,
        CancellationToken ct)
    {
        if (request.ExpectedVersion < 1
            || request.RequestId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length is < RefundRecallPolicy.ReasonMinimumLength or > RefundRecallPolicy.ReasonMaximumLength)
        {
            throw new PortalValidationException("Use a request reference and explain the refund decision in 10 to 1,000 characters.");
        }

        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var authorised = false;
        var commitStarted = false;
        var committed = false;
        Guid? financialRequest = null;
        Guid? countryClaimRequest = null;
        try
        {
            await LockOperations(c, tx, ct);
            await EnsureCanWork(c, tx, actor, dispute, ct);
            if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
                SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/FinanceRepository/SelectFinanceColumns",
                ("PermissionPredicate", WorkflowAccess.Permission("@ActorId", "refund.approve"))),
                new { ActorId = actor },
                tx,
                cancellationToken: ct)) != 1)
            {
                throw new PortalForbiddenException("Refund approval requires explicit financial authority.");
            }

            authorised = true;
            var previous = await c.QuerySingleOrDefaultAsync<RefundRow>(new CommandDefinition(
                RefundSql,
                new { Id = dispute },
                tx,
                cancellationToken: ct));
            if (previous is not null)
            {
                if (previous.Actor != actor || previous.RequestId != request.RequestId || previous.Reason != request.Reason.Trim())
                {
                    throw new PortalConflictException("This case already has a refund.");
                }

                await tx.CommitAsync(ct);
                return MapRefundApprovalPolicy(previous, true);
            }

            if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT
                    COUNT(*)
                FROM "disputes"."refunds" AS "dispute_refunds"
                WHERE "dispute_refunds"."requestId" = @RequestId
                """,
                new { request.RequestId },
                tx,
                cancellationToken: ct)) > 0)
            {
                throw new PortalConflictException("This request reference was already used for another refund.");
            }

            var row = await c.QuerySingleOrDefaultAsync<CaseRow>(new CommandDefinition(
                CaseSql,
                new { Id = dispute },
                tx,
                cancellationToken: ct)) ?? throw new PortalNotFoundException();
            if (row.Version != request.ExpectedVersion || row.Status != "UnderReview")
            {
                throw new PortalConflictException("Only the current Under review case can be refunded. Refresh the case.");
            }

            if (row.Amount >= servicePolicy.HighValueThreshold)
            {
                await RequireSupervisor(c, tx, actor, ct);
            }

            financialRequest = financialCommands is null ? null : request.RequestId;
            countryClaimRequest = financialCommands is null ? null : Guid.NewGuid();
            var result = await PostLocked(
                c,
                tx,
                row,
                actor,
                request.RequestId,
                request.Reason.Trim(),
                null,
                correlation,
                countryClaimRequest,
                ct);
            commitStarted = true;
            await tx.CommitAsync(ct);
            committed = true;
            logger?.LogInformation(
                "Refund workflow committed {DisputeId} {CreditId} {Amount} {Currency} {ActorId}",
                dispute,
                result.CreditTransactionId,
                result.Amount,
                result.Currency,
                actor);
            return result;
        }
        catch (Exception ex)
        {
            var rolledBack = !committed && await RollbackFinancialRequest(c, tx, financialRequest, countryClaimRequest, commitStarted);
            if (authorised && rolledBack)
            {
                try
                {
                    await using var logtx = await c.BeginTransactionAsync(CancellationToken.None);
                    await AuditOperation(
                        c,
                        logtx,
                        actor,
                        "RefundFailed",
                        dispute,
                        new
                        {
                            outcome = "Failed",
                            error = ex.GetType().Name,
                            correlation
                        },
                        CancellationToken.None,
                        "Failed");
                    await logtx.CommitAsync(CancellationToken.None);
                }
                catch (Exception auditEx)
                {
                    logger?.LogError(auditEx, "Failed to persist financial failure audit {DisputeId}", dispute);
                }
            }

            logger?.LogWarning("Refund rejected {DisputeId} {FailureType}", dispute, ex.GetType().Name);
            if (ex is DbException db && dialect.IsUniqueConstraintViolation(db))
            {
                throw new PortalConflictException("A conflicting financial record already exists. Refresh the case to confirm its outcome.");
            }

            throw;
        }
    }

    private async Task<bool> RollbackFinancialRequest(
        DbConnection connection,
        DbTransaction transaction,
        Guid? request,
        Guid? countryClaimRequest,
        bool commitStarted)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception failure)
        {
            logger?.LogWarning("Financial rollback could not be confirmed {RequestId} {FailureType}", request, failure.GetType().Name);
            return false;
        }

        if (!commitStarted && request is not null && countryClaimRequest is not null && financialCommands is not null)
        {
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(runtime.FinancialReconciliationTimeoutSeconds));
                await financialCommands.ReleaseUncommitted(connection, request.Value, countryClaimRequest.Value, deadline.Token);
            }
            catch (Exception failure)
            {
                logger?.LogWarning(
                    "Uncommitted financial country claim awaits reconciliation {RequestId} {FailureType}",
                    request,
                    failure.GetType().Name);
            }
        }

        return true;
    }

    public async Task<RefundRecallDto> RecallRefund(
        Guid actor,
        Guid dispute,
        RecallRefundRequest request,
        string correlation,
        CancellationToken ct)
    {
        if (request.ExpectedVersion < 1
            || request.RequestId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length is < RefundRecallPolicy.ReasonMinimumLength or > RefundRecallPolicy.ReasonMaximumLength)
        {
            throw new PortalValidationException("Use a request reference and explain the refund recall in 10 to 1,000 characters.");
        }

        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var commitStarted = false;
        var committed = false;
        Guid? financialRequest = null;
        Guid? countryClaimRequest = null;
        try
        {
            await LockOperations(c, tx, ct);
            await EnsureCanWork(c, tx, actor, dispute, ct);
            var previous = await c.QuerySingleOrDefaultAsync<RecallRow>(new CommandDefinition(
                RecallSql,
                new { Id = dispute },
                tx,
                cancellationToken: ct));
            if (previous is not null)
            {
                if (previous.Actor != actor
                    || previous.RequestId != request.RequestId
                    || previous.ExpectedVersion != request.ExpectedVersion
                    || previous.Reason != request.Reason.Trim())
                {
                    throw new PortalConflictException("This refund was already recalled. Refresh the case.");
                }

                commitStarted = true;
                await tx.CommitAsync(ct);
                committed = true;
                return MapRecall(previous, true);
            }

            if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT
                    COUNT(*)
                FROM "disputes"."refundRecalls" AS "dispute_refund_recalls"
                WHERE "dispute_refund_recalls"."requestId" = @RequestId
                """,
                new { request.RequestId },
                tx,
                cancellationToken: ct)) != 0)
            {
                throw new PortalConflictException("This request reference was already used for another refund recall.");
            }

            var refund = await c.QuerySingleOrDefaultAsync<RefundRow>(new CommandDefinition(
                RefundSql,
                new { Id = dispute },
                tx,
                cancellationToken: ct)) ?? throw new PortalConflictException("This case has no posted refund to recall.");
            var row = await c.QuerySingleAsync<CaseRow>(new CommandDefinition(CaseSql, new { Id = dispute }, tx, cancellationToken: ct));
            if (row.Version != request.ExpectedVersion || !CaseServicePolicy.IsClosed(row.Status))
            {
                throw new PortalConflictException("Refresh the closed case before recalling its refund.");
            }

            if (row.Amount >= servicePolicy.HighValueThreshold)
            {
                await RequireSupervisor(c, tx, actor, ct);
            }

            if (!await CanSettleHistorical(row, ct) || row.ExternalCount > 0 || row.Currency != row.AccountCurrency)
            {
                throw new PortalConflictException("Only an eligible owned account with a known original balance can have a refund recalled.");
            }

            if (financialCommands is not null)
            {
                financialRequest = request.RequestId;
                countryClaimRequest = Guid.NewGuid();
                var command = await financialCommands.CreateFinancialCommand(
                    c,
                    tx,
                    dispute,
                    refund.CreditTransactionId,
                    row.Account,
                    row.Customer,
                    "Recall",
                    refund.Amount,
                    refund.Currency,
                    actor,
                    request.RequestId,
                    request.Reason.Trim(),
                    null,
                    request.ExpectedVersion,
                    countryClaimRequest.Value,
                    ct);
                commitStarted = true;
                await tx.CommitAsync(ct);
                committed = true;
                return new(
                    command.TransactionId,
                    dispute,
                    refund.CreditTransactionId,
                    command.TransactionId,
                    refund.Amount,
                    refund.Currency,
                    null,
                    request.Reason.Trim(),
                    false,
                    "Processing");
            }

            await c.ExecuteScalarAsync<long>(new CommandDefinition(
                dialect.Provider == DatabaseProvider.SqlServer ? SqlScripts.ReadSqlScript("SqlServer/Queries/Api/FinanceRepository/LockAccountForFinancialReview") : SqlScripts.ReadSqlScript("PostgreSql/Queries/Api/FinanceRepository/LockAccountForFinancialReview"),
                new { row.Account },
                tx,
                cancellationToken: ct));
            var funds = await c.QuerySingleAsync<BalanceRow>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/GetAccountProjectedBalanceTotals"),
                new { row.Account },
                tx,
                cancellationToken: ct));
            if (!RefundRecallPolicy.HasAvailableFunds(refund.Amount, funds.CurrentAmount, funds.ReservedAmount, funds.Unknown == 0))
            {
                throw new PortalConflictException("The account has insufficient available funds for this full refund recall. No money has moved.");
            }

            var result = await ProjectRecallLocked(
                c,
                tx,
                row,
                refund,
                actor,
                request.RequestId,
                request.ExpectedVersion,
                request.Reason.Trim(),
                correlation,
                ct,
                null);
            commitStarted = true;
            await tx.CommitAsync(ct);
            committed = true;
            return result;
        }
        catch
        {
            if (!committed)
            {
                await RollbackFinancialRequest(c, tx, financialRequest, countryClaimRequest, commitStarted);
            }

            throw;
        }
    }

    private async Task<RefundRecallDto> ProjectRecallLocked(
        DbConnection c,
        DbTransaction tx,
        CaseRow row,
        RefundRow refund,
        Guid actor,
        Guid requestId,
        int expectedVersion,
        string suppliedReason,
        string correlation,
        CancellationToken ct,
        Guid? fixedReversal)
    {
        var dispute = row.Id;
        var reason = suppliedReason;
        var reversal = fixedReversal ?? Guid.NewGuid();
        var recallId = fixedReversal ?? Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/FinanceRepository/InsertProjectedRefundRecallTransaction",
            ("FalseValue", dialect.FalseLiteral)),
            new
            {
                Id = reversal,
                row.Account,
                Reference = "RR-" + reversal.ToString("N"),
                Now = now,
                Description = "Refund recalled - " + row.Reference + " - " + refund.CustomerReference,
                refund.Amount,
                refund.Currency
            },
            tx,
            cancellationToken: ct));
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/InsertRefundRecall"),
            new
            {
                Recall = recallId,
                Credit = refund.CreditTransactionId,
                Reversal = reversal,
                Actor = actor,
                Dispute = dispute,
                requestId,
                expectedVersion,
                refund.Amount,
                refund.Currency,
                Reason = reason,
                Now = now
            },
            tx,
            cancellationToken: ct));
        var changed = await c.ExecuteAsync(new CommandDefinition(
            """
            UPDATE "disputes"."disputes"
            SET
                "version" = "disputes"."version" + 1,
                "updatedAt" = @Now
            WHERE "disputes"."publicId" = @Id
            AND "disputes"."version" = @Version
            """,
            new
            {
                Id = dispute,
                Version = expectedVersion,
                Now = now
            },
            tx,
            cancellationToken: ct));
        if (changed != 1)
        {
            throw new PortalConflictException("The case changed before the refund recall was committed.");
        }

        await AuditOperation(
            c,
            tx,
            actor,
            "DisputeRefundRecalled",
            dispute,
            new
            {
                credit = refund.CreditTransactionId,
                reversal,
                refund.Amount,
                refund.Currency,
                requestId,
                expectedVersion,
                reason,
                correlation
            },
            ct);
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/InsertNote"),
            new
            {
                Id = Guid.NewGuid(),
                Notice = Guid.NewGuid(),
                Dispute = dispute,
                Actor = actor,
                row.Customer,
                Now = now,
                Body = "Your refund of " + refund.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " " + refund.Currency + " was recalled. Reason: " + reason + " You can reinstate this case for review."
            },
            tx,
            cancellationToken: ct));
        await DisputeNotificationOutbox.EnqueueEvent(
            c,
            tx,
            dispute,
            recallId,
            "RefundRecalled",
            "Refund recalled",
            (await RefundRecallNotification.GetRefundRecallNotificationDetails(
                c,
                tx,
                dialect,
                [dispute],
                templates ?? MessageTemplateCatalog.Default,
                ct))[dispute],
            null,
            ct);
        var result = MapRecall(await c.QuerySingleAsync<RecallRow>(new CommandDefinition(
            RecallSql,
            new { Id = dispute },
            tx,
            cancellationToken: ct)));
        return result;
    }

    private async Task<bool> CanSettleHistorical(CaseRow row, CancellationToken ct) => row.ActiveFlag == 1
        || financialCommands is not null
        && row.CustomerActiveFlag == 1
        && row.AccountStatus == "Blocked"
        && await financialCommands.CanSettleHistorical(row.Account, row.Customer, row.Currency, ct);

    private async Task<RefundDto> PostLocked(
        DbConnection c,
        DbTransaction tx,
        CaseRow row,
        Guid? actor,
        Guid request,
        string reason,
        int? policy,
        string correlation,
        Guid? countryClaimRequest,
        CancellationToken ct)
    {
        var accountEligible = row.ActiveFlag == 1 || actor is not null && await CanSettleHistorical(row, ct);
        if (!accountEligible
            || row.Amount <= 0
            || (financialCommands is null
            && row.Currency != "ZAR")
            || row.AccountCurrency != row.Currency
            || row.Direction != "Debit"
            || row.PaymentStatus != "Posted"
            || row.TransactionAt > DateTimeOffset.UtcNow
            || row.Type is "Refund" or "RefundReversal"
            || row.ExternalCount > 0)
        {
            throw new PortalConflictException("This payment cannot receive a local credit. Its bank must confirm any external refund.");
        }

        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/CountRefundsForTransaction"),
            new { row.TransactionId },
            tx,
            cancellationToken: ct)) > 0)
        {
            throw new PortalConflictException("This payment already has an outstanding refund. Refresh the case.");
        }

        if (financialCommands is not null)
        {
            var command = await financialCommands.CreateFinancialCommand(
                c,
                tx,
                row.Id,
                row.TransactionId,
                row.Account,
                row.Customer,
                "Refund",
                row.Amount,
                row.Currency,
                actor,
                request,
                reason,
                policy,
                row.Version,
                countryClaimRequest ?? throw new InvalidOperationException("The financial attempt claim is required."),
                ct);
            return new(
                row.Id,
                command.TransactionId,
                row.Amount,
                row.Currency,
                null,
                actor is null,
                false,
                "RF-" + command.TransactionId.ToString("N"),
                null,
                "Processing");
        }

        return await ProjectRefundLocked(c, tx, row, actor, request, reason, policy, correlation, ct, null);
    }

    private async Task<RefundDto> ProjectRefundLocked(
        DbConnection c,
        DbTransaction tx,
        CaseRow row,
        Guid? actor,
        Guid request,
        string reason,
        int? policy,
        string correlation,
        CancellationToken ct,
        Guid? fixedCredit)
    {
        var credit = fixedCredit ?? Guid.NewGuid();
        var customerReference = await NewReceiptReference(c, tx, ct);
        var now = DateTimeOffset.UtcNow;
        if (row.Status == "Submitted")
        {
            await c.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/InsertSystemReviewStartedHistory"),
                new
                {
                    Id = Guid.NewGuid(),
                    Case = row.Id,
                    Actor = actor,
                    Now = now,
                    Reason = reason
                },
                tx,
                cancellationToken: ct));
        }

        await AuditOperation(
            c,
            tx,
            actor,
            "DisputeRefundApproved",
            row.Id,
            new
            {
                row.TransactionId,
                row.Account,
                row.Amount,
                row.Currency,
                request,
                policy,
                outcome = "Approved",
                correlation
            },
            ct);
        now = DateTimeOffset.UtcNow;
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/FinanceRepository/InsertProjectedRefundCreditTransaction",
            ("FalseValue", dialect.FalseLiteral)),
            new
            {
                Id = credit,
                row.Account,
                Reference = "RF-" + credit.ToString("N"),
                Now = now,
                Description = "Dispute refund - " + row.Reference,
                row.Amount,
                row.Currency
            },
            tx,
            cancellationToken: ct));
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/InsertRefund"),
            new
            {
                row.Id,
                row.TransactionId,
                Credit = credit,
                row.Account,
                row.Customer,
                row.Amount,
                row.Currency,
                Actor = actor,
                Request = request,
                Reason = reason,
                Source = actor is null ? "System" : "Staff",
                Policy = policy,
                Now = now
            },
            tx,
            cancellationToken: ct));
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/InsertRefundReceipt"),
            new { row.Id, Reference = customerReference },
            tx,
            cancellationToken: ct));
        await AuditOperation(
            c,
            tx,
            actor,
            "DisputeRefundPosted",
            row.Id,
            new
            {
                row.TransactionId,
                row.Account,
                row.Amount,
                row.Currency,
                credit,
                outcome = "Success",
                previousStatus = row.Status,
                newStatus = "Resolved",
                policy,
                correlation
            },
            ct);
        var resolvedAt = DateTimeOffset.UtcNow;
        var updated = await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/MarkDisputesResolved"),
            new
            {
                row.Id,
                row.Version,
                Reason = reason,
                Now = resolvedAt
            },
            tx,
            cancellationToken: ct));
        if (updated != 1)
        {
            throw new PortalConflictException("The case changed before the refund was committed.");
        }

        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/InsertSystemRefundResolvedHistory"),
            new
            {
                Id = Guid.NewGuid(),
                Case = row.Id,
                Actor = actor,
                Now = resolvedAt,
                Reason = reason
            },
            tx,
            cancellationToken: ct));
        await DisputeNotificationOutbox.EnqueueEvent(
            c,
            tx,
            row.Id,
            credit,
            "RefundPosted",
            "Refund credited",
            "Your refund has been credited. Open your case for details.",
            null,
            ct);
        await SupportRepository.CloseCaseConversations(c, tx, row.Id, actor, dialect, ct);
        await DisputeNotificationOutbox.Enqueue(c, tx, row.Id, "Completed", ct);
        return MapRefundApprovalPolicy(await c.QuerySingleAsync<RefundRow>(new CommandDefinition(
            RefundSql,
            new { Id = row.Id },
            tx,
            cancellationToken: ct)));
    }

    private static async Task<string> NewReceiptReference(DbConnection c, DbTransaction tx, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var value = "RF-" + System.Security.Cryptography.RandomNumberGenerator.GetInt32(10000000, 100000000).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT
                    COUNT(*)
                FROM "disputes"."refundReceipts" AS "refund_receipts"
                WHERE "refund_receipts"."customerReference" = @Value
                """,
                new { Value = value },
                tx,
                cancellationToken: ct)) == 0)
            {
                return value;
            }
        }

        throw new PortalConflictException("A refund reference could not be reserved. Retry safely.");
    }

    public async Task<bool> ProcessAutomatic(CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var commitStarted = false;
        var committed = false;
        Guid? financialRequest = null;
        Guid? countryClaimRequest = null;
        try
        {
            await LockOperations(c, tx, ct);
            var evaluationTime = DateTimeOffset.UtcNow;
            var rules = await PolicyVersionRepository.EffectiveAutomatic(c, tx, evaluationTime, ct);
            if (rules is null || !rules.Enabled)
            {
                commitStarted = true;
                await tx.CommitAsync(ct);
                committed = true;
                return false;
            }

            var id = await c.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                SqlScripts.RenderSqlScript("Shared/Queries/Api/FinanceRepository/GetSubmittedDispute", ("FalseValue", dialect.FalseLiteral)),
                new { rules.Version },
                tx,
                cancellationToken: ct));
            if (id is null)
            {
                commitStarted = true;
                await tx.CommitAsync(ct);
                committed = true;
                return false;
            }

            var row = await c.QuerySingleAsync<CaseRow>(new CommandDefinition(CaseSql, new { Id = id }, tx, cancellationToken: ct));
            var original = await c.QuerySingleOrDefaultAsync<OriginalRow>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/GetConfirmedOriginalPaymentAndDuplicateProof"),
                new { Id = row.TransactionId },
                tx,
                cancellationToken: ct));
            var fraud = await c.ExecuteScalarAsync<int>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/CountDisputesForAccount"),
                new { row.Account },
                tx,
                cancellationToken: ct));
            var day = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
            var total = await c.ExecuteScalarAsync<decimal>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/SumPostedSystemRefundsForCustomerDay"),
                new
                {
                    row.Customer,
                    row.Currency,
                    Day = day,
                    End = day.AddDays(1)
                },
                tx,
                cancellationToken: ct));
            if (financialCommands is not null)
            {
                total += await c.ExecuteScalarAsync<decimal>(new CommandDefinition(
                    SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/SumQueuedSystemRefundsForCustomerDay"),
                    new
                    {
                        row.Customer,
                    row.Currency,
                        Day = day,
                        End = day.AddDays(1)
                    },
                    tx,
                    cancellationToken: ct));
            }

            var refunded = await c.ExecuteScalarAsync<int>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/CountRefunds"),
                new { Id = row.TransactionId },
                tx,
                cancellationToken: ct));
            var match = original is not null
                && original.Account == row.Account
                && original.Amount == row.Amount
                && original.Currency == row.Currency
                && original.Type == row.Type
                && original.Direction == "Debit"
                && original.Status == "Posted"
                && string.Equals(original.Merchant, original.DuplicateMerchant, StringComparison.OrdinalIgnoreCase);
            var debitOrder = row.Category == "DebitOrder";
            var ruleCode = debitOrder ? AutomaticRefundPolicy.DebitOrderRuleCode : AutomaticRefundPolicy.RuleCode;
            var assessmentEvidence = configuration is not null && environment is not null
                && DisputePortal.Platform.Infrastructure.AssessmentDataAccess.IsEnabled(configuration, environment)
                && await c.ExecuteScalarAsync<int>(new CommandDefinition(
                    SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/CountAssessmentTransactionEvidence"),
                    new
                    {
                        Transaction = row.TransactionId,
                        row.Account,
                        row.Customer,
                        Hash = row.TransactionId.ToString("N")
                    }, tx, cancellationToken: ct)) == 1;
            var trustedSource = row.ExternalCount == 0 && (row.LocalCount == 1 || assessmentEvidence);
            var trusted = debitOrder ? trustedSource : original?.Proof == "LocalBankFeed" && trustedSource;
            var facts = new AutomaticRefundFacts
            {
                Amount = row.Amount,
                Currency = row.Currency,
                AccountCurrency = row.AccountCurrency,
                Type = row.Type,
                Direction = row.Direction,
                Status = row.PaymentStatus,
                TransactionAt = row.TransactionAt,
                OriginalAt = original?.OriginalAt ?? DateTimeOffset.MinValue,
                Active = row.ActiveFlag == 1,
                ContactVerified = row.VerifiedFlag == 1,
                TrustedLocalProof = trusted,
                MatchesOriginal = match,
                OriginalDisputed = original?.Disputed > 0,
                FraudOpen = fraud > 0,
                AlreadyRefunded = refunded > 0,
                RefundedToday = total
            };
            var policy = new AutomaticRefundPolicy
            {
                Version = rules.Version,
                Enabled = rules.Enabled,
                ContactVerificationRequired = !AssessmentSecurity.AllowsUnverifiedContacts(configuration, environment),
                MaximumAmount = rules.MaximumAmount,
                MaximumAgeHours = rules.MaximumAgeHours,
                DuplicateWindowMinutes = rules.DuplicateWindowMinutes,
                DailyLimit = rules.DailyLimit
            };
            var approved = debitOrder ? policy.ApprovesDebitOrder(facts, row.Scheme, row.Mandate, evaluationTime) : policy.Approves(
                facts,
                evaluationTime);
            var reason = approved ? debitOrder ? "System reviewed the debit order against the saved refund policy and approved the refund." : "System verified the confirmed duplicate against the original payment and approved the refund." : "Automatic checks did not establish eligibility; a qualified specialist must review this case.";
            await c.ExecuteAsync(new CommandDefinition(
                SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/FinanceRepository/InsertAutomaticDecision",
                ("AuditMetadataValueExpression", dialect.AuditMetadataValueExpression("@Facts"))),
                new
                {
                    Id = Guid.NewGuid(),
                    Case = row.Id,
                    Rule = ruleCode,
                    rules.Version,
                    rules.VersionPublicId,
                    Outcome = approved ? "RefundApproved" : "ManualReview",
                    Reason = reason,
                    Facts = JsonSerializer.Serialize(new
                    {
                        row.TransactionId,
                        row.Account,
                        originalTransaction = original?.Id,
                        facts,
                        assessmentEvidence,
                        contactVerificationRequired = policy.ContactVerificationRequired,
                        evaluatedAt = evaluationTime,
                        policyVersionPublicId = rules.VersionPublicId
                    })
                },
                tx,
                cancellationToken: ct));
            await AuditOperation(
                c,
                tx,
                null,
                "AutomaticDisputeDecision",
                row.Id,
                new
                {
                    rule = ruleCode,
                    rules.Version,
                    outcome = approved ? "RefundApproved" : "ManualReview",
                    row.TransactionId
                },
                ct);
            if (approved)
            {
                var reviewAt = evaluationTime;
                await c.ExecuteAsync(new CommandDefinition(
                    SqlScripts.ReadSqlScript("Shared/Queries/Api/FinanceRepository/InsertStatusHistoryByHistoryAndIdAndNowAndReasonAndVersion"),
                    new
                    {
                        History = Guid.NewGuid(),
                        row.Id,
                        row.Version,
                        Now = reviewAt,
                        Reason = reason
                    },
                    tx,
                    cancellationToken: ct));
                row.Status = "UnderReview";
                row.Version++;
                await DisputeNotificationOutbox.EnqueueEvent(
                    c,
                    tx,
                    row.Id,
                    Guid.NewGuid(),
                    "SystemReview",
                    "System Review Started",
                    "The system is reviewing your eligible dispute refund.",
                    null,
                    ct);
                var request = Guid.NewGuid();
                financialRequest = financialCommands is null ? null : request;
                countryClaimRequest = financialCommands is null ? null : Guid.NewGuid();
                await PostLocked(
                    c,
                    tx,
                    row,
                    null,
                    request,
                    reason,
                    rules.Version,
                    Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString(),
                    countryClaimRequest,
                    ct);
            }

            commitStarted = true;
            await tx.CommitAsync(ct);
            committed = true;
            logger?.LogInformation(
                "System decision {DisputeId} {Rule} {Version} {Outcome}",
                row.Id,
                ruleCode,
                rules.Version,
                approved ? "RefundApproved" : "ManualReview");
            if (realtime is not null)
            {
                await realtime.PublishPortalUpdate([row.Customer], true, ["disputes", "banking", "notifications"], row.Id, ct);
            }

            return true;
        }
        catch
        {
            if (!committed)
            {
                await RollbackFinancialRequest(c, tx, financialRequest, countryClaimRequest, commitStarted);
            }

            throw;
        }
    }

    public async Task ApplyFinancialOutcome(DbConnection c, DbTransaction tx, DomainEvent value, CancellationToken ct)
    {
        var outcome = value.DeserializePayload<DisputePortal.Platform.Contracts.TransactionEvent>();
        var command = await c.QuerySingleOrDefaultAsync<DisputeFinancialCommand>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/FinanceRepository/GetFinancialCommandById",
            ("SelectQuery", DisputeFinancialCommandService.Select)),
            new { Id = outcome.TransactionId },
            tx,
            cancellationToken: ct));
        if (command is null || command.State is "Posted" or "Failed")
        {
            return;
        }

        if (command.UserId != outcome.UserId
            || command.AccountId != outcome.AccountId
            || command.Amount != outcome.Amount
            || command.Currency != outcome.Currency
            || command.Kind != outcome.Kind)
        {
            throw new InvalidOperationException("The ledger outcome conflicts with its approved case instruction.");
        }

        if (value.EventType == DisputePortal.Platform.Contracts.EventTypes.TransactionFailed)
        {
            await c.ExecuteAsync(new CommandDefinition(
                """
                UPDATE "disputes"."financialCommands"
                SET
                    "state" = 'Failed',
                    "failureCode" = @Code,
                    "completedAt" = @Now
                WHERE "financialCommands"."publicId" = @Id
                """,
                new
                {
                    Id = command.TransactionId,
                    Code = outcome.FailureCode ?? "financial_request_failed",
                    Now = DateTimeOffset.UtcNow
                },
                tx,
                cancellationToken: ct));
            await AuditOperation(
                c,
                tx,
                command.ActorId,
                "DisputeFinancialRequestFailed",
                command.DisputeId,
                new { command.TransactionId, code = outcome.FailureCode },
                ct);
            return;
        }

        await c.ExecuteAsync(new CommandDefinition(
            """
            UPDATE "disputes"."financialCommands"
            SET
                "state" = 'Finalizing'
            WHERE "financialCommands"."publicId" = @Id
            """,
            new { Id = command.TransactionId },
            tx,
            cancellationToken: ct));
        var row = await c.QuerySingleAsync<CaseRow>(new CommandDefinition(
            CaseSql,
            new { Id = command.DisputeId },
            tx,
            cancellationToken: ct));
        if (command.Kind == "Refund")
        {
            await ProjectRefundLocked(
                c,
                tx,
                row,
                command.ActorId,
                command.RequestId,
                command.Reason,
                command.PolicyVersion,
                command.Correlation.ToString(),
                ct,
                command.TransactionId);
        }
        else
        {
            var refund = await c.QuerySingleAsync<RefundRow>(new CommandDefinition(
                RefundSql,
                new { Id = command.DisputeId },
                tx,
                cancellationToken: ct));
            await ProjectRecallLocked(
                c,
                tx,
                row,
                refund,
                command.ActorId ?? throw new InvalidOperationException("A recall requires its staff actor."),
                command.RequestId,
                command.ExpectedVersion,
                command.Reason,
                command.Correlation.ToString(),
                ct,
                command.TransactionId);
        }

        await c.ExecuteAsync(new CommandDefinition(
            """
            UPDATE "disputes"."financialCommands"
            SET
                "state" = 'Posted',
                "completedAt" = @Now
            WHERE "financialCommands"."publicId" = @Id
            """,
            new { Id = command.TransactionId, Now = DateTimeOffset.UtcNow },
            tx,
            cancellationToken: ct));
    }

    private class RefundRow
    {
        public Guid DisputeId { get; set; }
        public Guid CreditTransactionId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public DateTimeOffset PostedAt { get; set; }
        public string Source { get; set; } = string.Empty;
        public Guid? Actor { get; set; }
        public Guid RequestId { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string CreditReference { get; set; } = string.Empty;
        public string CustomerReference { get; set; } = string.Empty;

        public RefundRow()
        {
        }

        public RefundRow(
            Guid DisputeId,
            Guid CreditTransactionId,
            decimal Amount,
            string Currency,
            DateTimeOffset PostedAt,
            string Source,
            Guid? Actor,
            Guid RequestId,
            string Reason,
            string CreditReference,
            string CustomerReference)
        {
            this.DisputeId = DisputeId;
            this.CreditTransactionId = CreditTransactionId;
            this.Amount = Amount;
            this.Currency = Currency;
            this.PostedAt = PostedAt;
            this.Source = Source;
            this.Actor = Actor;
            this.RequestId = RequestId;
            this.Reason = Reason;
            this.CreditReference = CreditReference;
            this.CustomerReference = CustomerReference;
        }
    }

    private class RecallRow
    {
        public Guid Id { get; set; }
        public Guid DisputeId { get; set; }
        public Guid CreditTransactionId { get; set; }
        public Guid ReversalTransactionId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public DateTimeOffset PostedAt { get; set; }
        public string Reason { get; set; } = string.Empty;
        public Guid Actor { get; set; }
        public Guid RequestId { get; set; }
        public int ExpectedVersion { get; set; }

        public RecallRow()
        {
        }

        public RecallRow(
            Guid Id,
            Guid DisputeId,
            Guid CreditTransactionId,
            Guid ReversalTransactionId,
            decimal Amount,
            string Currency,
            DateTimeOffset PostedAt,
            string Reason,
            Guid Actor,
            Guid RequestId,
            int ExpectedVersion)
        {
            this.Id = Id;
            this.DisputeId = DisputeId;
            this.CreditTransactionId = CreditTransactionId;
            this.ReversalTransactionId = ReversalTransactionId;
            this.Amount = Amount;
            this.Currency = Currency;
            this.PostedAt = PostedAt;
            this.Reason = Reason;
            this.Actor = Actor;
            this.RequestId = RequestId;
            this.ExpectedVersion = ExpectedVersion;
        }
    }

    private class CaseRow
    {
        public Guid Id { get; set; }
        public Guid Customer { get; set; }
        public Guid TransactionId { get; set; }
        public Guid Account { get; set; }
        public int Version { get; set; }
        public string Status { get; set; } = "";
        public string Reference { get; set; } = "";
        public string Category { get; set; } = "";
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "";
        public string AccountCurrency { get; set; } = "";
        public string Direction { get; set; } = "";
        public string PaymentStatus { get; set; } = "";
        public string Type { get; set; } = "";
        public DateTimeOffset TransactionAt { get; set; }
        public string? Scheme { get; set; }
        public string? Mandate { get; set; }
        public int ExternalCount { get; set; }
        public int LocalCount { get; set; }
        public string AccountStatus { get; set; } = "";
        public int CustomerActiveFlag { get; set; }
        public int ActiveFlag { get; set; }
        public int VerifiedFlag { get; set; }
    }

    private class BalanceRow
    {
        public Guid AccountId { get; set; }
        public string Currency { get; set; } = "";
        public decimal CurrentAmount { get; set; }
        public decimal ReservedAmount { get; set; }
        public int Unknown { get; set; }
        public string Status { get; set; } = "";
        public string AccountType { get; set; } = "";
    }

    private class DecisionRow
    {
        public string Outcome { get; set; } = string.Empty;
        public string RuleCode { get; set; } = string.Empty;
        public int PolicyVersion { get; set; }

        public DecisionRow()
        {
        }

        public DecisionRow(string Outcome, string RuleCode, int PolicyVersion)
        {
            this.Outcome = Outcome;
            this.RuleCode = RuleCode;
            this.PolicyVersion = PolicyVersion;
        }
    }

    private class ActivityRow
    {
        public Guid Id { get; set; }
        public string Action { get; set; } = "";
        public string Actor { get; set; } = "";
        public string Source { get; set; } = "";
        public string? SafeMetadata { get; set; }
        public DateTimeOffset OccurredAt { get; set; }
        public string? Detail { get; set; }
    }

    private class OriginalRow
    {
        public Guid Id { get; set; }
        public Guid Account { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "";
        public string Merchant { get; set; } = "";
        public string Type { get; set; } = "";
        public string Direction { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTimeOffset OriginalAt { get; set; }
        public string Proof { get; set; } = "";
        public string DuplicateMerchant { get; set; } = "";
        public int Disputed { get; set; }
    }
}
