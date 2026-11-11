using Dapper;
using DisputePortal.Api.Service.Banking;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Service.Disputes;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Models.Domain;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Banking;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;

public class DisputeRepository(
    IDatabaseConnectionFactory factory,
    IDatabaseDialect databaseDialect,
    TimeZoneInfo businessTimeZone,
    CaseServicePolicy servicePolicy,
    DisputeOperationsRepository operations,
    DisputeRiskRepository riskRepository,
    IPortalSettingsRepository portalSettings,
    ILogger<DisputeRepository>? logger = null,
    ICustomerTransactionPresentation? presentation = null) : RepositoryBase(factory, databaseDialect), IDisputeRepository
{
    private static readonly string CustomerDisputeSelect = SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/SelectCustomerDisputeColumns");
    private static readonly string CustomerDisputeFrom = SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/JoinCustomerDisputeTransactionDetails");

    public async Task<CreateDisputeResult> CreateDispute(
        Guid customerId,
        CreateDisputeCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnection(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await LockOperations(connection, transaction, cancellationToken);
            if (command.StaffActorId is { } staffActor)
            {
                await RequireAssistanceStaff(connection, transaction, staffActor, cancellationToken);
            }

            if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT
                    COUNT(*)
                FROM "identity"."users" AS "users"
                WHERE "users"."publicId" = @Customer
                AND "users"."accountStatus" = 'Active'
                AND "users"."deletedAt" IS NULL
                """,
                new { Customer = customerId },
                transaction,
                cancellationToken: cancellationToken)) != 1)
            {
                throw new PortalNotFoundException();
            }

            var replay = await connection.QuerySingleOrDefaultAsync<ReplayRow>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/GetDisputeForCustomer"),
                new { CustomerId = customerId, Key = idempotencyKey },
                transaction,
                cancellationToken: cancellationToken));
            if (replay is not null)
            {
                if (replay.SpecialistReview != command.RequestSpecialistReview
                    || (replay.SubmittedBy ?? customerId) != (command.StaffActorId ?? customerId)
                    || replay.TransactionId != command.TransactionId
                    || !string.Equals(replay.Category, command.Category, StringComparison.Ordinal)
                    || !string.Equals(replay.Reason, command.Reason, StringComparison.Ordinal)
                    || !string.Equals(replay.Description, command.Description, StringComparison.Ordinal))
                {
                    throw new PortalConflictException("This submission key was already used with different dispute details. Review your request and use a new key.");
                }

                await transaction.CommitAsync(cancellationToken);
                return new CreateDisputeResult(replay.Id, replay.Reference, replay.Status, true);
            }

            var eligible = await connection.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition(
                SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/DisputeRepository/GetOwnedTransactionForDisputeSubmission",
                ("EligibleTransactionExpression", dialect.EligibleTransactionExpression)),
                new { command.TransactionId, CustomerId = customerId },
                transaction,
                cancellationToken: cancellationToken));
            if (eligible is null)
            {
                throw new PortalNotFoundException("The transaction was not found.");
            }

            if (!eligible.Value)
            {
                throw new PortalConflictException("This transaction is not eligible for a dispute.");
            }

            var categoryId = await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/GetEnabledCategoryIdForTransactionType"),
                new
                {
                    command.Category,
                    command.TransactionId,
                    Enabled = true
                },
                transaction,
                cancellationToken: cancellationToken));
            if (categoryId is null)
            {
                throw new PortalValidationException("Select a valid dispute category for this transaction type.", "category");
            }

            var risk = command.StaffActorId is null && !command.RequestSpecialistReview ? await riskRepository.EnforceRisk(
                connection,
                transaction,
                customerId,
                command.TransactionId,
                command.SessionId,
                command.VerificationId,
                cancellationToken) : await riskRepository.AssessLocked(
                connection,
                transaction,
                customerId,
                command.TransactionId,
                cancellationToken);
            if (!risk.Allowed && !(command.RequestSpecialistReview && risk.AssistedReview))
            {
                throw new PortalConflictException(risk.Message);
            }

            if (command.RequestSpecialistReview && command.StaffActorId is null && (risk.Allowed || !risk.AssistedReview))
            {
                throw new PortalConflictException("Use the standard dispute submission for this transaction.");
            }

            var actor = command.StaffActorId ?? customerId;
            var id = Guid.NewGuid();
            var reference = $"DP-{DateTimeOffset.UtcNow:yyyy}-{RandomNumberGenerator.GetInt32(0, 1_000_000):D6}";
            await connection.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/InsertCustomerDispute"),
                new
                {
                    Id = id,
                    Reference = reference,
                    command.TransactionId,
                    CustomerId = customerId,
                    CategoryId = categoryId.Value,
                    PolicySnapshot = risk.PolicyVersionPublicId,
                    command.Reason,
                    command.Description,
                    Actor = actor,
                    SpecialistReview = command.RequestSpecialistReview,
                    Key = idempotencyKey
                },
                transaction,
                cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/InsertSubmittedDisputeStatusHistory"),
                new
                {
                    Id = Guid.NewGuid(),
                    DisputeId = id,
                    CustomerId = customerId,
                    Actor = actor,
                    Comment = command.StaffActorId is null ? "Dispute submitted." : "Dispute submitted by a service consultant on behalf of the customer."
                },
                transaction,
                cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/InsertDisputeSubmittedNotification"),
                new
                {
                    Id = Guid.NewGuid(),
                    CustomerId = customerId,
                    DisputeId = id
                },
                transaction,
                cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/LinkOpenTransactionConversationsToDispute"),
                new
                {
                    DisputeId = id,
                    command.TransactionId,
                    CustomerId = customerId
                },
                transaction,
                cancellationToken: cancellationToken));
            var classification = await operations.InitializeCase(connection, transaction, id, command.Category, cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/ResetOpenCaseConversationRouting"),
                new { Id = id, Queue = classification.QueueType },
                transaction,
                cancellationToken: cancellationToken));
            await AuditOperation(
                connection,
                transaction,
                actor,
                "DisputeRiskAssessed",
                id,
                new
                {
                    risk.PolicyVersion,
                    risk.PolicyVersionPublicId,
                    risk.RequiresVerification,
                    risk.AssistedReview,
                    command.VerificationId,
                    CustomerId = customerId,
                    Intake = command.StaffActorId is null ? "Customer" : "Staff"
                },
                cancellationToken);
            if (risk.AssistedReview || command.StaffActorId is not null)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/InsertInitialCustomerDisputeNote"),
                    new
                    {
                        Id = Guid.NewGuid(),
                        Dispute = id,
                        Actor = actor,
                        ReviewNote = risk.AssistedReview ? "This collection has been referred for specialist investigation. No reversal is automatic or guaranteed." : "A service consultant submitted this dispute on behalf of the customer."
                    },
                    transaction,
                    cancellationToken: cancellationToken));
            }

            await operations.DispatchLocked(connection, transaction, cancellationToken);
            await DisputeNotificationOutbox.Enqueue(connection, transaction, id, "Started", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger?.LogInformation(
                "Case workflow started {CaseId} {TransactionType} {Category} {Queue} {Severity} {Priority} {Status}",
                id,
                classification.TransactionType,
                command.Category,
                classification.QueueType,
                classification.Severity,
                classification.Priority,
                "Submitted");
            return new CreateDisputeResult(id, reference, "Submitted");
        }
        catch (DbException ex) when (dialect.IsUniqueConstraintViolation(ex))
        {
            await RollbackAfterFailure(transaction);
            throw new PortalConflictException("An active dispute already exists for this transaction, or this request key was already used.");
        }
        catch
        {
            await RollbackAfterFailure(transaction);
            throw;
        }
    }

    public async Task<PageResult<DisputeSummaryDto>> GetCustomerDisputes(
        Guid customerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken,
        Guid? accountId = null,
        string? status = null,
        string? search = null,
        DisputePortal.Api.Models.Internal.HistoryFilter? filter = null)
    {
        filter ??= new global::DisputePortal.Api.Models.Internal.HistoryFilter();
        filter = new global::DisputePortal.Api.Models.Internal.HistoryFilter(filter)
        {
            Status = filter.Status ?? status
        };
        filter.ValidateHistoryFilter();
        if (search?.Length > 100)
        {
            throw new PortalValidationException("Keep searches within 100 characters.");
        }

        var settings = await portalSettings.GetPortalSettings(cancellationToken);
        var closedSince = DateTimeOffset.UtcNow.AddDays(-settings.Settings.ClientOptions["closedCaseHistoryDays"]);
        var where = SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/FilterCustomerDisputesByAccountAndHistory");
        where += SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/DisputeRepository/FilterCustomerDisputesBySearch",
            ("SearchPredicate", dialect.LikeExpression("d.reference", "@SearchPattern")),
            ("RelatedSearchPredicate", dialect.LikeExpression("t.\"merchantName\"", "@SearchPattern")),
            ("AdditionalSearchPredicate", dialect.LikeExpression("t.description", "@SearchPattern")));
        var args = new
        {
            SearchPattern = CleanSearch(search)?.ToLowerInvariant(),
            Status = string.IsNullOrEmpty(filter.Status) ? status : filter.Status,
            From = filter.StartUtc(businessTimeZone),
            To = filter.EndUtc(businessTimeZone),
            AccountId = accountId,
            ClosedSince = closedSince,
            CustomerId = customerId,
            Offset = PageOffset(page, pageSize),
            PageSize = pageSize
        };
        var sql = SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/DisputeRepository/GetDisputePage",
            ("FilterClause", where),
            ("PaginationClause", dialect.PaginationClause),
            ("RelatedFilterClause", where));
        await using var connection = await connections.OpenConnection(cancellationToken);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(sql, args, cancellationToken: cancellationToken));
        var data = (await results.ReadAsync<DisputeRow>()).ToArray();
        var display = await (presentation ?? CustomerTransactionPresentation.Default).LoadTransactionPresentationRules(cancellationToken);
        var items = data.Select(row => MapCaseAssignmentSummary(row, display)).ToArray();
        var total = await results.ReadSingleAsync<long>();
        return new PageResult<DisputeSummaryDto>(items, page, pageSize, total);
    }

    public async Task<DisputeDetailDto?> GetCustomerDispute(Guid customerId, Guid disputeId, CancellationToken cancellationToken)
    {
        var sql = SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/GetDisputeByCustomerIdAndDisputeId");
        await using var connection = await connections.OpenConnection(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<DisputeRow>(new CommandDefinition(
            sql,
            new { CustomerId = customerId, DisputeId = disputeId },
            cancellationToken: cancellationToken));
        if (row is null)
        {
            return null;
        }

        var txn = await connection.QuerySingleAsync<TransactionInfo>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/GetDisputeTransactionDetails"),
            new { Id = row.TransactionId },
            cancellationToken: cancellationToken));
        var history = await GetHistory(connection, null, disputeId, false, cancellationToken);
        var notes = await GetNotes(connection, null, disputeId, customerVisibleOnly: true, cancellationToken);
        var display = await (presentation ?? CustomerTransactionPresentation.Default).LoadTransactionPresentationRules(cancellationToken);
        return new DisputeDetailDto(MapCaseAssignmentSummary(row, display), null, txn.MerchantName, txn.TransactionAt, history, notes)
        {
            ResolvedBy = ClosedBy(row.Status, history)
        };
    }

    public async Task<DashboardDto> GetDashboard(CancellationToken cancellationToken, Guid? actorId = null)
    {
        var sql = SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/DisputeRepository/GetCaseWorkQueueSummary",
            ("StaffScopeSql", StaffScopeSql));
        await using var connection = await connections.OpenConnection(cancellationToken);
        var localDay = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, businessTimeZone).Date;
        var dayStart = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localDay, businessTimeZone), TimeSpan.Zero);
        return await connection.QuerySingleAsync<DashboardDto>(new CommandDefinition(
            sql,
            new { DayStartUtc = dayStart, ActorId = actorId ?? Guid.Empty },
            cancellationToken: cancellationToken));
    }

    public async Task<PageResult<AdminQueueItemDto>> GetAdminQueue(
        AdminQueueFilter filter,
        CancellationToken cancellationToken,
        Guid? actorId = null)
    {
        var dates = new HistoryFilter
        {
            Status = filter.Status,
            From = filter.From,
            To = filter.To
        };
        dates.ValidateHistoryFilter();
        if (filter.Priority is < 1 or > 5 || filter.Search?.Length > 100)
        {
            throw new PortalValidationException("Check the queue filters.");
        }

        var settings = await portalSettings.GetPortalSettings(cancellationToken);
        var closedSince = DateTimeOffset.UtcNow.AddDays(-settings.Settings.ClientOptions["closedCaseHistoryDays"]);
        var where = SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/DisputeRepository/FilterStaffDisputesByAssignmentAndSearch",
            ("StaffScopeSql", StaffScopeSql),
            ("SearchPredicate", dialect.LikeExpression("d.reference", "@SearchPattern")),
            ("RelatedSearchPredicate", dialect.LikeExpression("p.\"displayName\"", "@SearchPattern")),
            ("AdditionalSearchPredicate", dialect.LikeExpression("t.\"merchantName\"", "@SearchPattern")));
        var search = CleanSearch(filter.Search)?.ToLowerInvariant();
        var args = new
        {
            ActorId = actorId ?? Guid.Empty,
            ClosedSince = closedSince,
            filter.Status,
            filter.Priority,
            From = dates.StartUtc(businessTimeZone),
            To = dates.EndUtc(businessTimeZone),
            SearchPattern = search,
            Offset = PageOffset(filter.Page, filter.PageSize),
            filter.PageSize
        };
        var sql = SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/DisputeRepository/GetPagedStaffDisputesAndTotalCount",
            ("FilterClause", where),
            ("PaginationClause", dialect.PaginationClause),
            ("RelatedFilterClause", where));
        await using var connection = await connections.OpenConnection(cancellationToken);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(sql, args, cancellationToken: cancellationToken));
        var rows = (await results.ReadAsync<AdminFlatRow>()).ToArray();
        var total = await results.ReadSingleAsync<long>();
        var caseMetadata = rows.Length == 0 ? [] : (await connection.QueryAsync<CaseOperationsDto>(new CommandDefinition(
            CaseSelect + " WHERE " + dialect.InListExpression("d.\"publicId\"", "@Ids"),
            new { Ids = rows.Select(row => row.Id).ToArray() },
            cancellationToken: cancellationToken))).ToArray();
        if (actorId.HasValue)
        {
            await operations.DescribeActions(connection, actorId.Value, caseMetadata, cancellationToken);
        }
        else
        {
            foreach (var item in caseMetadata)
                item.Actions.IsClosed = CaseServicePolicy.IsClosed(item.Status);
        }

        var display = await (presentation ?? CustomerTransactionPresentation.Default).LoadTransactionPresentationRules(cancellationToken);
        var items = rows.Select(row => new AdminQueueItemDto(MapQueue(row, display)) { Operations = caseMetadata.FirstOrDefault(x => x.Id == row.Id) }).ToArray();
        return new PageResult<AdminQueueItemDto>(items, filter.Page, filter.PageSize, total);
    }

    private static readonly string AdminSelect = SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/SelectStaffDisputeColumns");
    private static readonly string AdminFrom = SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/JoinStaffDisputeCustomerAndAccountDetails");

    public async Task<AdminDisputeDto?> GetAdminDispute(Guid disputeId, CancellationToken cancellationToken, Guid? actorId = null)
    {
        await using var connection = await connections.OpenConnection(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<AdminFlatRow>(new CommandDefinition(
            SqlScripts.RenderSqlScript("Shared/Queries/Api/DisputeRepository/GetDisputeByDisputeId", ("StaffScopeSql", StaffScopeSql)),
            new { DisputeId = disputeId, ActorId = actorId ?? Guid.Empty },
            cancellationToken: cancellationToken));
        if (row is null)
        {
            return null;
        }

        var history = await GetHistory(connection, null, disputeId, true, cancellationToken);
        var notes = await GetNotes(connection, null, disputeId, false, cancellationToken);
        var display = await (presentation ?? CustomerTransactionPresentation.Default).LoadTransactionPresentationRules(cancellationToken);
        return new AdminDisputeDto(MapAdmin(row, history, notes, display))
        {
            Operations = await operations.GetCase(actorId ?? Guid.Empty, disputeId, cancellationToken),
            ResolvedBy = ClosedBy(row.Status, history)
        };
    }

    public async Task<int> TransitionDispute(
        Guid actorId,
        Guid disputeId,
        TransitionCommand command,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnection(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await LockOperations(connection, transaction, cancellationToken);
            await EnsureCanWork(connection, transaction, actorId, disputeId, cancellationToken);
            var current = await connection.QuerySingleOrDefaultAsync<LockedDispute>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/GetDisputeStateForStatusTransition"),
                new { Id = disputeId },
                transaction,
                cancellationToken: cancellationToken));
            if (current is null)
            {
                throw new PortalNotFoundException();
            }

            if (current.Version != command.ExpectedVersion)
            {
                throw new PortalConflictException("This dispute changed since it was opened. Refresh and review the latest status.");
            }

            if (!Enum.TryParse<DisputeStatus>(current.Status, out var from)
                || !Enum.TryParse<DisputeStatus>(command.Status, out var to)
                || !DisputeWorkflow.CanTransition(from, to))
            {
                throw new PortalConflictException($"A dispute cannot move from {current.Status} to {command.Status}.");
            }

            var reopening = from is DisputeStatus.Resolved or DisputeStatus.Declined or DisputeStatus.Withdrawn;
            if (reopening)
            {
                await RequireSupervisor(connection, transaction, actorId, cancellationToken, true);
                if (string.IsNullOrWhiteSpace(command.Comment) || command.Comment.Trim().Length is < 10 or > 1000)
                {
                    throw new PortalValidationException("Explain why this case must be reopened.");
                }

                if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/CountPostedDisputeRefunds"),
                    new { Id = disputeId },
                    transaction,
                    cancellationToken: cancellationToken)) > 0)
                {
                    throw new PortalConflictException("A refunded case retains its financial history. After a recall, the customer can reinstate it as a linked case.");
                }

                if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/CountOtherActiveDisputesForTransaction"),
                    new { Id = disputeId },
                    transaction,
                    cancellationToken: cancellationToken)) > 0)
                {
                    throw new PortalConflictException("This transaction already has an active case.");
                }
            }

            var now = DateTimeOffset.UtcNow;
            var resolution = to is DisputeStatus.Resolved or DisputeStatus.Declined ? command.Comment : null;
            var updated = await connection.ExecuteAsync(new CommandDefinition(
                SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/DisputeRepository/UpdateDisputeStatusAndResolution",
                ("TrueValue", dialect.TrueLiteral),
                ("RelatedTrueValue", dialect.TrueLiteral),
                ("AdditionalTrueValue", dialect.TrueLiteral),
                ("AdditionalTrueValue3", dialect.TrueLiteral)),
                new
                {
                    Reopening = reopening,
                    Status = to.ToString(),
                    Now = now,
                    Terminal = to is DisputeStatus.Resolved or DisputeStatus.Declined,
                    Resolution = resolution,
                    Id = disputeId,
                    ExpectedVersion = command.ExpectedVersion
                },
                transaction,
                cancellationToken: cancellationToken));
            if (updated != 1)
            {
                throw new PortalConflictException("This dispute changed since it was opened. Refresh and review the latest status.");
            }

            await connection.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/InsertDisputeTransitionHistory"),
                new
                {
                    HistoryId = Guid.NewGuid(),
                    DisputeId = disputeId,
                    Previous = from.ToString(),
                    Next = to.ToString(),
                    ActorId = actorId,
                    command.Comment
                },
                transaction,
                cancellationToken: cancellationToken));
            await AddAudit(
                connection,
                transaction,
                actorId,
                reopening ? "DisputeReopened" : "DisputeStatusChanged",
                "dispute",
                disputeId,
                correlationId,
                JsonSerializer.Serialize(new
                {
                    from = from.ToString(),
                    to = to.ToString(),
                    version = command.ExpectedVersion + 1,
                    reason = command.Comment?.Trim()
                }),
                cancellationToken);
            if (!reopening)
            {
                var title = to switch
                {
                    DisputeStatus.UnderReview => "Case under review",
                    DisputeStatus.AwaitingCustomer => "More information requested",
                    DisputeStatus.Resolved => "Dispute resolved",
                    DisputeStatus.Declined => "Dispute outcome available",
                    DisputeStatus.Withdrawn => "Case stopped",
                    _ => "Case updated"
                };
                var body = to == DisputeStatus.UnderReview ? "Your case is under review by the support team." : command.Comment?.Trim();
                if (string.IsNullOrWhiteSpace(body))
                {
                    body = $"Your dispute status is now {to}.";
                }

                var recipients = await WorkflowRecipients(connection, transaction, disputeId, cancellationToken);
                await DisputeNotificationOutbox.EnqueueEvent(
                    connection,
                    transaction,
                    disputeId,
                    Guid.NewGuid(),
                    "DisputeStatusChanged",
                    title,
                    body,
                    recipients.Append(actorId),
                    cancellationToken);
            }

            if (CaseServicePolicy.IsClosed(to.ToString()))
            {
                await SupportRepository.CloseCaseConversations(connection, transaction, disputeId, actorId, dialect, cancellationToken);
                await DisputeNotificationOutbox.Enqueue(connection, transaction, disputeId, "Completed", cancellationToken);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE "disputes"."disputes"
                SET
                    "firstResponseAt" = COALESCE("disputes"."firstResponseAt", @Now),
                    "slaClosedAt" = @ClosedAt
                WHERE "disputes"."publicId" = @Id
                """,
                new
                {
                    Now = now,
                    ClosedAt = CaseServicePolicy.IsClosed(to.ToString()) ? (DateTimeOffset?)now : null,
                    Id = disputeId
                },
                transaction,
                cancellationToken: cancellationToken));
            if (reopening)
            {
                var priority = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    """
                    SELECT
                        "transaction_disputes"."priority" AS "priority"
                    FROM "disputes"."disputes" AS "transaction_disputes"
                    WHERE "transaction_disputes"."publicId" = @Id
                    """,
                    new { Id = disputeId },
                    transaction,
                    cancellationToken: cancellationToken));
                var targets = servicePolicy.Targets(priority);
                await connection.ExecuteAsync(new CommandDefinition(
                    SqlScripts.RenderSqlScript(
                    "Shared/Queries/Api/DisputeRepository/ResetDisputeWorkflowForReopening",
                    ("FalseValue", dialect.FalseLiteral)),
                    new
                    {
                        Id = disputeId,
                        ResponseDue = now.AddHours(targets.Response),
                        Due = now.AddHours(targets.Resolution)
                    },
                    transaction,
                    cancellationToken: cancellationToken));
                await operations.DispatchLocked(connection, transaction, cancellationToken);
                var recipients = await WorkflowRecipients(connection, transaction, disputeId, cancellationToken);
                await DisputeNotificationOutbox.EnqueueEvent(
                    connection,
                    transaction,
                    disputeId,
                    Guid.NewGuid(),
                    "DisputeReopened",
                    "Case reopened",
                    "Your case was reopened for specialist review. Open it to follow progress.",
                    recipients,
                    cancellationToken);
            }

            var finalVersion = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT
                    "transaction_disputes"."version" AS "version"
                FROM "disputes"."disputes" AS "transaction_disputes"
                WHERE "transaction_disputes"."publicId" = @Id
                """,
                new { Id = disputeId },
                transaction,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            logger?.LogInformation(
                "Case status changed {CaseId} {PreviousStatus} {Status} {Version}",
                disputeId,
                from.ToString(),
                to.ToString(),
                finalVersion);
            return finalVersion;
        }
        catch
        {
            await RollbackAfterFailure(transaction);
            throw;
        }
    }

    public async Task<CreateDisputeResult> ReinstateCustomerDispute(
        Guid customerId,
        Guid disputeId,
        DisputePortal.Api.Models.Requests.ReinstateDisputeRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedVersion < 1
            || request.RequestId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length is < 10 or > 1000)
        {
            throw new PortalValidationException("Use a request reference and explain the reinstatement in 10–1,000 characters.");
        }

        await using var connection = await connections.OpenConnection(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockOperations(connection, transaction, cancellationToken);
        var current = await connection.QuerySingleOrDefaultAsync<LockedDispute>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/GetOwnedDisputeStateForReinstatement"),
            new { Id = disputeId, Customer = customerId },
            transaction,
            cancellationToken: cancellationToken)) ?? throw new PortalNotFoundException();
        var replay = await connection.QueryFirstOrDefaultAsync<ReinstateReplay>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/GetReinstatementsForRequest"),
            new { Id = disputeId, request.RequestId },
            transaction,
            cancellationToken: cancellationToken));
        if (replay is not null)
        {
            if (replay.ParentId != disputeId
                || replay.CustomerId != customerId
                || replay.RequestId != request.RequestId
                || replay.ExpectedVersion != request.ExpectedVersion
                || replay.Reason != request.Reason.Trim())
            {
                throw new PortalConflictException("This case was already reinstated, or this request reference was used with different details.");
            }

            await transaction.CommitAsync(cancellationToken);
            return new(replay.ChildId, replay.Reference, replay.Status, true);
        }

        if (current.Version != request.ExpectedVersion)
        {
            throw new PortalConflictException("This case changed. Refresh before reinstating it.");
        }

        var recalled = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/CountRefundRecalls"),
            new { Id = disputeId },
            transaction,
            cancellationToken: cancellationToken)) == 1;
        if (!RefundRecallPolicy.CanReinstate(current.Status, recalled, false))
        {
            throw new PortalConflictException("A case can only be reinstated after its refund has been recalled.");
        }

        var unavailable = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/CountUnavailableDisputeAccountOrTransaction"),
            new { Id = disputeId },
            transaction,
            cancellationToken: cancellationToken));
        if (unavailable != 0)
        {
            throw new PortalConflictException("This transaction has an active case, processing financial operation or outstanding refund, or its account is no longer active.");
        }

        var id = Guid.NewGuid();
        var reference = $"DP-{DateTimeOffset.UtcNow:yyyy}-{RandomNumberGenerator.GetInt32(0, 1_000_000):D6}";
        var now = DateTimeOffset.UtcNow;
        var reason = request.Reason.Trim();
        await connection.ExecuteAsync(new CommandDefinition(
            SqlScripts.RenderSqlScript("Shared/Queries/Api/DisputeRepository/InsertDispute", ("TrueValue", dialect.TrueLiteral)),
            new
            {
                Child = id,
                Reference = reference,
                Reason = reason,
                Key = "reinstate-" + request.RequestId.ToString("N"),
                Customer = customerId,
                Parent = disputeId
            },
            transaction,
            cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/InsertReinstatement"),
            new
            {
                Parent = disputeId,
                Child = id,
                request.RequestId,
                request.ExpectedVersion,
                Reason = reason,
                Now = now
            },
            transaction,
            cancellationToken: cancellationToken));
        var parentChanged = await connection.ExecuteAsync(new CommandDefinition(
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
                Id = disputeId,
                Version = request.ExpectedVersion,
                Now = now
            },
            transaction,
            cancellationToken: cancellationToken));
        if (parentChanged != 1)
        {
            throw new PortalConflictException("The case changed before reinstatement was committed.");
        }

        await connection.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/InsertStatusHistory"),
            new
            {
                Id = disputeId,
                Version = request.ExpectedVersion,
                Parent = disputeId,
                Child = id,
                Customer = customerId,
                History = Guid.NewGuid(),
                Note = Guid.NewGuid(),
                Now = now,
                Comment = "Reinstated as " + reference + " after the refund recall. " + reason
            },
            transaction,
            cancellationToken: cancellationToken));
        await operations.InitializeCase(
            connection,
            transaction,
            id,
            await connection.QuerySingleAsync<string>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/GetOriginalDisputeForCustomerReinstatement"),
                new { Id = id },
                transaction,
                cancellationToken: cancellationToken)),
            cancellationToken);
        await AddAudit(
            connection,
            transaction,
            customerId,
            "DisputeReinstated",
            "dispute",
            disputeId,
            correlationId,
            JsonSerializer.Serialize(new
            {
                parentDisputeId = disputeId,
                childDisputeId = id,
                request.RequestId,
                request.ExpectedVersion,
                reason
            }),
            cancellationToken);
        await AddAudit(
            connection,
            transaction,
            customerId,
            "DisputeReinstated",
            "dispute",
            id,
            correlationId,
            JsonSerializer.Serialize(new
            {
                parentDisputeId = disputeId,
                childDisputeId = id,
                request.RequestId,
                reason
            }),
            cancellationToken);
        await operations.DispatchLocked(connection, transaction, cancellationToken);
        var recipients = await connection.QueryAsync<Guid>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/GetActiveUsers"),
            new { Id = id },
            transaction,
            cancellationToken: cancellationToken));
        await DisputeNotificationOutbox.EnqueueEvent(
            connection,
            transaction,
            id,
            id,
            "DisputeReinstated",
            "Case reinstated",
            "Your case was reinstated for specialist review after the refund recall. Open the case to follow its progress.",
            recipients,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(id, reference, "Submitted");
    }

    public async Task<int> WithdrawCustomerDispute(
        Guid customerId,
        Guid disputeId,
        int expectedVersion,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (expectedVersion < 1)
        {
            throw new PortalValidationException("Use the current case version.");
        }

        await using var connection = await connections.OpenConnection(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockOperations(connection, transaction, cancellationToken);
        var current = await connection.QuerySingleOrDefaultAsync<LockedDispute>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/GetOwnedDisputeStateForWithdrawal"),
            new { Id = disputeId, Customer = customerId },
            transaction,
            cancellationToken: cancellationToken)) ?? throw new PortalNotFoundException();
        if (current.Version != expectedVersion)
        {
            throw new PortalConflictException("This case changed. Refresh before stopping it.");
        }

        if (!Enum.TryParse<DisputeStatus>(current.Status, out var status) || !DisputeWorkflow.CanCustomerWithdraw(status))
        {
            throw new PortalConflictException("Only an active case can be stopped.");
        }

        const string explanation = "Customer withdrew and stopped this case.";
        var now = DateTimeOffset.UtcNow;
        var changed = await connection.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/WithdrawOwnedCustomerDispute"),
            new
            {
                Id = disputeId,
                Customer = customerId,
                Version = expectedVersion,
                Explanation = explanation,
                Now = now
            },
            transaction,
            cancellationToken: cancellationToken));
        if (changed != 1)
        {
            throw new PortalConflictException("This case changed. Refresh before stopping it.");
        }

        await connection.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/InsertCustomerWithdrawalStatusHistory"),
            new
            {
                Id = Guid.NewGuid(),
                Note = Guid.NewGuid(),
                Notification = Guid.NewGuid(),
                Dispute = disputeId,
                Previous = current.Status,
                Customer = customerId,
                Now = now,
                Explanation = explanation
            },
            transaction,
            cancellationToken: cancellationToken));
        await AddAudit(
            connection,
            transaction,
            customerId,
            "DisputeWithdrawn",
            "dispute",
            disputeId,
            correlationId,
            JsonSerializer.Serialize(new
            {
                from = current.Status,
                to = "Withdrawn",
                version = expectedVersion + 1
            }),
            cancellationToken);
        await DisputeNotificationOutbox.EnqueueEvent(
            connection,
            transaction,
            disputeId,
            Guid.NewGuid(),
            "DisputeWithdrawn",
            "Case stopped",
            explanation,
            await WorkflowRecipients(connection, transaction, disputeId, cancellationToken),
            cancellationToken);
        await SupportRepository.CloseCaseConversations(connection, transaction, disputeId, customerId, dialect, cancellationToken);
        await DisputeNotificationOutbox.Enqueue(connection, transaction, disputeId, "Completed", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return expectedVersion + 1;
    }

    private static async Task<IReadOnlyList<Guid>> WorkflowRecipients(DbConnection c, DbTransaction tx, Guid dispute, CancellationToken ct) =>
        (await c.QueryAsync<Guid>(new CommandDefinition(
        SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/GetActiveUsers"),
        new { Id = dispute },
        tx,
        cancellationToken: ct))).AsList();

    private static ActionActorDto? ClosedBy(string status, IReadOnlyList<StatusHistoryDto> history) =>
        CaseServicePolicy.IsClosed(status) ? history.FirstOrDefault(entry => entry.NewStatus == status)?.ActorIdentity : null;

    public async Task AddNote(Guid actorId, Guid disputeId, NoteCommand command, string correlationId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnection(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await LockOperations(connection, transaction, cancellationToken);
            await EnsureCanWork(connection, transaction, actorId, disputeId, cancellationToken);
            var status = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
                """
                SELECT
                    "transaction_disputes"."status" AS "status"
                FROM "disputes"."disputes" AS "transaction_disputes"
                WHERE "transaction_disputes"."publicId" = @Id
                """,
                new { Id = disputeId },
                transaction,
                cancellationToken: cancellationToken));
            if (!DisputeWorkflow.CanSendCaseMessage(status))
            {
                throw new PortalConflictException("Closed cases are available as read-only history.");
            }

            var customerId = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/GetDisputeCustomerId"),
                new { Id = disputeId },
                transaction,
                cancellationToken: cancellationToken));
            if (customerId is null)
            {
                throw new PortalNotFoundException();
            }

            var noteId = Guid.NewGuid();
            await connection.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/InsertNote"),
                new
                {
                    Id = noteId,
                    DisputeId = disputeId,
                    ActorId = actorId,
                    command.Content,
                    command.Visibility
                },
                transaction,
                cancellationToken: cancellationToken));
            await AddAudit(
                connection,
                transaction,
                actorId,
                "DisputeNoteAdded",
                "dispute",
                disputeId,
                correlationId,
                JsonSerializer.Serialize(new { visibility = command.Visibility }),
                cancellationToken);
            if (command.Visibility == "Customer")
            {
                var assigned = await connection.QueryAsync<Guid>(new CommandDefinition(
                    SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/GetAssignedCaseOwnerId"),
                    new { Id = disputeId },
                    transaction,
                    cancellationToken: cancellationToken));
                await DisputeNotificationOutbox.EnqueueEvent(
                    connection,
                    transaction,
                    disputeId,
                    noteId,
                    "DisputeMessage",
                    "New dispute update",
                    "There is a new update on your dispute. Open the case to read it.",
                    assigned.Append(actorId),
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await RollbackAfterFailure(transaction);
            throw;
        }
    }

    private DisputeSummaryDto MapCaseAssignmentSummary(DisputeRow x, ICustomerTransactionPresentation display) => new(
        x.Id,
        x.Reference,
        x.TransactionId,
        display.Description(x.TransactionType, x.MerchantName, x.TransactionDescription),
        x.Amount,
        x.Currency,
        x.Category,
        x.Status,
        x.Priority,
        x.Version,
        x.CreatedAt,
        x.Resolution,
        x.TransactionType,
        display.TypeLabel(x.TransactionType));

    private AdminQueueItemDto MapQueue(AdminFlatRow x, ICustomerTransactionPresentation display)
    {
        var summary = new DisputeSummaryDto(
            x.Id,
            x.Reference,
            x.TransactionId,
            x.TransactionDescription,
            x.Amount,
            x.Currency,
            x.Category,
            x.Status,
            x.Priority,
            x.Version,
            x.CreatedAt,
            x.Resolution,
            x.TransactionType,
            display.TypeLabel(x.TransactionType));
        return new AdminQueueItemDto(
            summary,
            x.CustomerName,
            x.MaskedEmail,
            x.AccountNumber,
            x.TransactionReference,
            x.MerchantName,
            x.TransactionAt);
    }

    private AdminDisputeDto MapAdmin(
        AdminFlatRow x,
        IReadOnlyList<StatusHistoryDto> history,
        IReadOnlyList<DisputeNoteDto> notes,
        ICustomerTransactionPresentation display)
    {
        var summary = new DisputeSummaryDto(
            x.Id,
            x.Reference,
            x.TransactionId,
            x.TransactionDescription,
            x.Amount,
            x.Currency,
            x.Category,
            x.Status,
            x.Priority,
            x.Version,
            x.CreatedAt,
            x.Resolution,
            x.TransactionType,
            display.TypeLabel(x.TransactionType));
        return new AdminDisputeDto(
            summary,
            x.CustomerName,
            x.MaskedEmail,
            x.AccountNumber,
            x.TransactionReference,
            x.MerchantName,
            x.TransactionAt,
            history,
            notes);
    }

    private async Task<IReadOnlyList<StatusHistoryDto>> GetHistory(
        DbConnection connection,
        DbTransaction? transaction,
        Guid disputeId,
        bool internalDetails,
        CancellationToken ct)
    {
        var rows = (await connection.QueryAsync<HistoryRow>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/GetStatusHistoryForDispute"),
            new { DisputeId = disputeId },
            transaction,
            cancellationToken: ct))).AsList();
        var identities = await ActorIdentityReader.GetRecordedActorIdentities(
            connection,
            transaction,
            dialect,
            "History",
            rows.Select(row => row.Id),
            internalDetails,
            ct);
        return rows.Select(row =>
        {
            identities.TryGetValue(row.Id, out var identity);
            return new StatusHistoryDto(
                row.PreviousStatus,
                row.NewStatus,
                identity?.Label ?? "Actor not recorded",
                row.ChangedAt,
                row.Comment,
                identity);
        }).ToArray();
    }

    private async Task<IReadOnlyList<DisputeNoteDto>> GetNotes(
        DbConnection connection,
        DbTransaction? transaction,
        Guid disputeId,
        bool customerVisibleOnly,
        CancellationToken ct)
    {
        var visibility = customerVisibleOnly ? SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRepository/FilterCustomerVisibleCaseNotes") : "";
        var rows = (await connection.QueryAsync<NoteRow>(new CommandDefinition(
            SqlScripts.RenderSqlScript("Shared/Queries/Api/DisputeRepository/GetNotesForDispute", ("Visibility", visibility)),
            new { DisputeId = disputeId },
            transaction,
            cancellationToken: ct))).AsList();
        var identities = await ActorIdentityReader.GetRecordedActorIdentities(
            connection,
            transaction,
            dialect,
            "Note",
            rows.Select(row => row.Id),
            !customerVisibleOnly,
            ct);
        return rows.Select(row =>
        {
            identities.TryGetValue(row.Id, out var identity);
            return new DisputeNoteDto(identity?.Label ?? "Actor not recorded", row.Content, row.Visibility, row.CreatedAt, identity);
        }).ToArray();
    }

    // A provider may already have completed the transaction after a timeout or failed commit.
    // Cleanup must not replace that original failure, including an uncertain commit outcome.
    private async Task RollbackAfterFailure(DbTransaction transaction)
    {
        try
        {
            if (transaction.Connection is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
        }
        catch (Exception cleanup) when (cleanup is DbException or InvalidOperationException)
        {
            logger?.LogWarning(
                "Rollback cleanup could not finish ({FailureType}); the original operation failure is preserved.",
                cleanup.GetType().Name);
        }
    }

    private Task<int> AddAudit(
        DbConnection connection,
        DbTransaction transaction,
        Guid actorId,
        string action,
        string entityType,
        Guid entityId,
        string correlationId,
        string safeMetadata,
        CancellationToken ct) => connection.ExecuteAsync(new CommandDefinition(
        SqlScripts.RenderSqlScript(
        "Shared/Queries/Api/DisputeRepository/InsertAuditEvent",
        ("AuditMetadataValueExpression", dialect.AuditMetadataValueExpression("@SafeMetadata"))),
        new
        {
            Id = Guid.NewGuid(),
            ActorId = actorId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            CorrelationId = correlationId,
            SafeMetadata = safeMetadata
        },
        transaction,
        cancellationToken: ct));

    private class ReplayRow
    {
        public Guid Id { get; set; }
        public string Reference { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public Guid TransactionId { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Guid? SubmittedBy { get; set; }
        public bool SpecialistReview { get; set; }

        public ReplayRow()
        {
        }

        public ReplayRow(
            Guid Id,
            string Reference,
            string Status,
            Guid TransactionId,
            string Category,
            string Reason,
            string Description,
            Guid? SubmittedBy,
            bool SpecialistReview)
        {
            this.Id = Id;
            this.Reference = Reference;
            this.Status = Status;
            this.TransactionId = TransactionId;
            this.Category = Category;
            this.Reason = Reason;
            this.Description = Description;
            this.SubmittedBy = SubmittedBy;
            this.SpecialistReview = SpecialistReview;
        }
    }

    private class TransactionInfo
    {
        public string TransactionReference { get; set; } = string.Empty;
        public string MerchantName { get; set; } = string.Empty;
        public DateTimeOffset TransactionAt { get; set; }
        public string TransactionType { get; set; } = string.Empty;

        public TransactionInfo()
        {
        }

        public TransactionInfo(string TransactionReference, string MerchantName, DateTimeOffset TransactionAt, string TransactionType)
        {
            this.TransactionReference = TransactionReference;
            this.MerchantName = MerchantName;
            this.TransactionAt = TransactionAt;
            this.TransactionType = TransactionType;
        }
    }

    private class LockedDispute
    {
        public string Status { get; set; } = string.Empty;
        public int Version { get; set; }
        public Guid CustomerId { get; set; }

        public LockedDispute()
        {
        }

        public LockedDispute(string Status, int Version, Guid CustomerId)
        {
            this.Status = Status;
            this.Version = Version;
            this.CustomerId = CustomerId;
        }
    }

    private class DisputeRow
    {
        public Guid Id { get; set; }
        public string Reference { get; set; } = string.Empty;
        public Guid TransactionId { get; set; }
        public string TransactionDescription { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int Priority { get; set; }
        public int Version { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string? Resolution { get; set; }
        public string TransactionType { get; set; } = string.Empty;
        public string MerchantName { get; set; } = string.Empty;

        public DisputeRow()
        {
        }

        public DisputeRow(
            Guid Id,
            string Reference,
            Guid TransactionId,
            string TransactionDescription,
            decimal Amount,
            string Currency,
            string Category,
            string Status,
            int Priority,
            int Version,
            DateTimeOffset CreatedAt,
            string? Resolution,
            string TransactionType,
            string MerchantName)
        {
            this.Id = Id;
            this.Reference = Reference;
            this.TransactionId = TransactionId;
            this.TransactionDescription = TransactionDescription;
            this.Amount = Amount;
            this.Currency = Currency;
            this.Category = Category;
            this.Status = Status;
            this.Priority = Priority;
            this.Version = Version;
            this.CreatedAt = CreatedAt;
            this.Resolution = Resolution;
            this.TransactionType = TransactionType;
            this.MerchantName = MerchantName;
        }
    }

    private class AdminFlatRow
    {
        public Guid Id { get; set; }
        public string Reference { get; set; } = string.Empty;
        public Guid TransactionId { get; set; }
        public string TransactionDescription { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int Priority { get; set; }
        public int Version { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string? Resolution { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string MaskedEmail { get; set; } = string.Empty;
        public string AccountNumber { get; set; } = string.Empty;
        public string TransactionReference { get; set; } = string.Empty;
        public string MerchantName { get; set; } = string.Empty;
        public DateTimeOffset TransactionAt { get; set; }
        public string TransactionType { get; set; } = string.Empty;

        public AdminFlatRow()
        {
        }

        public AdminFlatRow(
            Guid Id,
            string Reference,
            Guid TransactionId,
            string TransactionDescription,
            decimal Amount,
            string Currency,
            string Category,
            string Status,
            int Priority,
            int Version,
            DateTimeOffset CreatedAt,
            string? Resolution,
            string CustomerName,
            string MaskedEmail,
            string AccountNumber,
            string TransactionReference,
            string MerchantName,
            DateTimeOffset TransactionAt,
            string TransactionType)
        {
            this.Id = Id;
            this.Reference = Reference;
            this.TransactionId = TransactionId;
            this.TransactionDescription = TransactionDescription;
            this.Amount = Amount;
            this.Currency = Currency;
            this.Category = Category;
            this.Status = Status;
            this.Priority = Priority;
            this.Version = Version;
            this.CreatedAt = CreatedAt;
            this.Resolution = Resolution;
            this.CustomerName = CustomerName;
            this.MaskedEmail = MaskedEmail;
            this.AccountNumber = AccountNumber;
            this.TransactionReference = TransactionReference;
            this.MerchantName = MerchantName;
            this.TransactionAt = TransactionAt;
            this.TransactionType = TransactionType;
        }
    }

    private class ReinstateReplay
    {
        public Guid ParentId { get; set; }
        public Guid ChildId { get; set; }
        public string Reference { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public Guid CustomerId { get; set; }
        public Guid RequestId { get; set; }
        public int ExpectedVersion { get; set; }
        public string Reason { get; set; } = string.Empty;

        public ReinstateReplay()
        {
        }

        public ReinstateReplay(
            Guid ParentId,
            Guid ChildId,
            string Reference,
            string Status,
            Guid CustomerId,
            Guid RequestId,
            int ExpectedVersion,
            string Reason)
        {
            this.ParentId = ParentId;
            this.ChildId = ChildId;
            this.Reference = Reference;
            this.Status = Status;
            this.CustomerId = CustomerId;
            this.RequestId = RequestId;
            this.ExpectedVersion = ExpectedVersion;
            this.Reason = Reason;
        }
    }

    private class HistoryRow
    {
        public Guid Id { get; set; }
        public string? PreviousStatus { get; set; }
        public string NewStatus { get; set; } = string.Empty;
        public DateTimeOffset ChangedAt { get; set; }
        public string? Comment { get; set; }

        public HistoryRow()
        {
        }

        public HistoryRow(Guid Id, string? PreviousStatus, string NewStatus, DateTimeOffset ChangedAt, string? Comment)
        {
            this.Id = Id;
            this.PreviousStatus = PreviousStatus;
            this.NewStatus = NewStatus;
            this.ChangedAt = ChangedAt;
            this.Comment = Comment;
        }
    }

    private class NoteRow
    {
        public Guid Id { get; set; }
        public string Content { get; set; } = string.Empty;
        public string Visibility { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }

        public NoteRow()
        {
        }

        public NoteRow(Guid Id, string Content, string Visibility, DateTimeOffset CreatedAt)
        {
            this.Id = Id;
            this.Content = Content;
            this.Visibility = Visibility;
            this.CreatedAt = CreatedAt;
        }
    }
}
