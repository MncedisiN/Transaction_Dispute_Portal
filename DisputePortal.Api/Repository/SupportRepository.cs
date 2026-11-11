using Dapper;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;
using System.Data.Common;
using System.Text.Json;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Repository.Data;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;

public partial class SupportRepository(
    IDatabaseConnectionFactory factory,
    IDatabaseDialect databaseDialect,
    TimeZoneInfo businessTimeZone,
    TimeProvider? timeProvider = null) : RepositoryBase(factory, databaseDialect), ISupportRepository, ISupportReadRepository
{
    public async Task<PageResult<SupportCaseOptionDto>> GetRelatedCases(
        Guid actor,
        int page,
        int pageSize,
        string? reference,
        CancellationToken ct)
    {
        if (reference?.Length > 100)
        {
            throw new PortalValidationException("Keep dispute reference searches within 100 characters.");
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var search = CleanSearch(reference);
        // Substring functions treat %, _ and brackets as literal reference
        // characters, rather than allowing LIKE wildcards to broaden access.
        var referenceMatch = dialect.Provider == DatabaseProvider.PostgreSql ? "POSITION(LOWER(@Reference) IN LOWER(d.reference))>0" : "CHARINDEX(LOWER(@Reference),LOWER(d.reference))>0";
        var where = SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/SupportRepository/JoinCasesVisibleToConversationActor",
            ("StaffScopeSql", StaffScopeSql),
            ("ReferenceMatch", referenceMatch));
        var sql = SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/SupportRepository/GetPagedConversationCasesAndTotalCount",
            ("FilterClause", where),
            ("PaginationClause", dialect.PaginationClause),
            ("RelatedFilterClause", where));
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var args = new
        {
            ActorId = actor,
            Reference = search,
            Now = now,
            Offset = PageOffset(page, pageSize),
            PageSize = pageSize
        };
        await using var connection = await connections.OpenConnection(ct);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(sql, args, cancellationToken: ct));
        var items = (await results.ReadAsync<SupportCaseOptionDto>()).AsList();
        return new PageResult<SupportCaseOptionDto>(items, page, pageSize, await results.ReadSingleAsync<long>());
    }

    private static readonly string CanReadRelatedCase = SqlScripts.RenderSqlScript(
        "Shared/Queries/Api/SupportRepository/SelectSupportColumnsWithCustomerId",
        ("CaseReadScope", WorkflowAccess.CaseReadScope()));
    private static readonly string ThreadFrom = SqlScripts.RenderSqlScript(
        "Shared/Queries/Api/SupportRepository/JoinSupportDetailsWithInnerJoins",
        ("CanReadRelatedCase", CanReadRelatedCase));
    private static readonly string ThreadSelect = SqlScripts.RenderSqlScript(
        "Shared/Queries/Api/SupportRepository/SelectConversationDetailsWithLinkedContextAndReadState",
        ("CanReadRelatedCase", CanReadRelatedCase),
        ("RelatedCanReadRelatedCase", CanReadRelatedCase),
        ("ThreadFrom", ThreadFrom));
    private static readonly string ThreadScope = "(" + WorkflowAccess.ReadScope() + ")";

    public async Task<PageResult<SupportThreadDto>> GetThreads(
        Guid actor,
        CancellationToken ct,
        Guid? disputeId = null,
        string? search = null,
        Guid? transactionId = null,
        DisputePortal.Api.Models.Internal.HistoryFilter? filter = null,
        Guid? relatedDisputeId = null,
        int page = 1,
        int pageSize = 100,
        bool assignedOnly = false,
        bool ownOnly = false)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        filter ??= new global::DisputePortal.Api.Models.Internal.HistoryFilter();
        filter.ValidateHistoryFilter();
        if (search?.Length > 100)
        {
            throw new PortalValidationException("Keep conversation searches within 100 characters.");
        }

        await using var c = await connections.OpenConnection(ct);
        var searchClause = $"(@SearchPattern IS NULL OR {dialect.LikeExpression("d.reference", "@SearchPattern")} OR {dialect.LikeExpression(
            "t.\"merchantName\"",
            "@SearchPattern")} OR {dialect.LikeExpression("t.description", "@SearchPattern")} OR {dialect.LikeExpression(
            "st.subject",
            "@SearchPattern")} OR {dialect.LikeExpression("st.\"relatedCaseContext\"", "@SearchPattern")})";
        var where = SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/SupportRepository/FilterSupportConversationsByCaseSearchAndStatus",
            ("ThreadScope", ThreadScope),
            ("SearchClause", searchClause));
        var sql = SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/SupportRepository/GetPagedConversationsAndTotalCount",
            ("ThreadSelect", ThreadSelect),
            ("FilterClause", where),
            ("PaginationClause", dialect.PaginationClause),
            ("ThreadFrom", ThreadFrom),
            ("RelatedFilterClause", where));
        using var results = await c.QueryMultipleAsync(new CommandDefinition(
            sql,
            new
            {
                ActorId = actor,
                Offset = PageOffset(page, pageSize),
                PageSize = pageSize,
                AssignedOnly = assignedOnly ? 1 : 0,
                OwnOnly = ownOnly ? 1 : 0,
                DisputeId = disputeId,
                RelatedDisputeId = relatedDisputeId,
                TransactionId = transactionId,
                SearchPattern = CleanSearch(search)?.ToLowerInvariant(),
                Status = string.IsNullOrEmpty(filter.Status) ? null : filter.Status,
                From = filter.StartUtc(businessTimeZone),
                To = filter.EndUtc(businessTimeZone),
                UnreadOnly = filter.UnreadOnly ? 1 : 0
            },
            cancellationToken: ct));
        var rows = (await results.ReadAsync<SupportThreadDto>()).AsList();
        var total = await results.ReadSingleAsync<long>();
        results.Dispose();
        await Decorate(c, null, actor, rows, ct);
        return new PageResult<SupportThreadDto>(rows, page, pageSize, total);
    }

    public async Task<IReadOnlyList<SupportMessageDto>> GetMessages(
        Guid actor,
        Guid thread,
        CancellationToken ct,
        long? afterSequence = null,
        long? beforeSequence = null) =>
        (await GetMessagePage(actor, thread, ct, afterSequence, beforeSequence)).Items;

    public async Task<SupportMessagePageDto> GetMessagePage(
        Guid actor,
        Guid thread,
        CancellationToken ct,
        long? afterSequence = null,
        long? beforeSequence = null)
    {
        if (afterSequence < 0 || beforeSequence < 1 || (afterSequence.HasValue && beforeSequence.HasValue))
        {
            throw new PortalValidationException("Use a valid message position.");
        }

        await using var c = await connections.OpenConnection(ct);
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.RenderSqlScript("Shared/Queries/Api/SupportRepository/CountConversationsById", ("ThreadScope", ThreadScope)),
            new { Id = thread, ActorId = actor },
            cancellationToken: ct)) == 0)
        {
            throw new PortalNotFoundException();
        }

        const int pageSize = 200;
        var order = afterSequence.HasValue ? "ASC" : "DESC";
        var sql = SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/SupportRepository/GetMessages",
            ("AuditMetadataSelectExpression", dialect.AuditMetadataSelectExpression("m.\"enquiryDetails\"")),
            ("Order", order));
        var rows = (await c.QueryAsync<SupportMessageDto>(new CommandDefinition(sql, new
        {
            Id = thread,
            After = afterSequence,
            Before = beforeSequence,
            Take = pageSize + 1
        }, cancellationToken: ct))).AsList();
        var hasMore = rows.Count > pageSize;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }
        const bool internalDetails = false;
        var identities = await ActorIdentityReader.GetRecordedActorIdentities(
            c,
            null,
            dialect,
            "Message",
            rows.Select(row => row.Id),
            internalDetails,
            ct);
        var conversationOwner = await c.ExecuteScalarAsync<Guid>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/SupportRepository/GetConversation"),
            new { Thread = thread },
            cancellationToken: ct));
        foreach (var row in rows)
        {
            identities.TryGetValue(row.Id, out var identity);
            row.Enquiry = row.EnquiryJson is null ? null : JsonSerializer.Deserialize<EnquirySnapshot>(
                row.EnquiryJson,
                JsonSerializerOptions.Web);
            row.ActorIdentity = identity;
            row.AuthorRole = row.AuthorId == conversationOwner ? "Customer" : "Support";
            row.AuthorName = identity?.Label ?? "Actor not recorded";
        }

        return new SupportMessagePageDto
        {
            Items = afterSequence.HasValue ? rows : rows.AsEnumerable().Reverse().ToArray(),
            HasMore = hasMore
        };
    }

    private static async Task EnsureConversationOpen(
        DbConnection c,
        DbTransaction tx,
        Guid? disputeId,
        Guid? transactionId,
        Guid? relatedDisputeId,
        CancellationToken ct)
    {
        if (relatedDisputeId.HasValue)
        {
            return;
        }

        string? status = null;
        if (disputeId is { } exact)
        {
            status = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                """
                SELECT
                    "transaction_disputes"."status" AS "status"
                FROM "disputes"."disputes" AS "transaction_disputes"
                WHERE "transaction_disputes"."publicId" = @Id
                """,
                new { Id = exact },
                tx,
                cancellationToken: ct));
        }
        else if (transactionId is { } transaction)
        {
            status = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/SupportRepository/GetLinkedCaseStatusForConversation"),
                new { Id = transaction },
                tx,
                cancellationToken: ct));
        }

        if ((disputeId.HasValue || status is not null) && !DisputeWorkflow.CanSendCaseMessage(status))
        {
            throw new PortalConflictException("This case is closed. Its conversation is available as read-only history.");
        }
    }

    public async Task<Guid> SendMessage(Guid actor, SupportSendRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Body)
            || request.Body.Trim().Length is < 2 or > 2000
            || string.IsNullOrWhiteSpace(request.Subject)
            || request.Subject.Length > 200)
        {
            throw new PortalValidationException("Add a subject and a message between 2 and 2,000 characters.");
        }

        if (request.ClientMessageId == Guid.Empty)
        {
            throw new PortalValidationException("Use a valid message request reference.");
        }

        if (request.RelatedDisputeId.HasValue && (request.DisputeId.HasValue || request.TransactionId.HasValue))
        {
            throw new PortalValidationException("Keep a general enquiry separate from case messages.");
        }

        var enquiryHash = EnquiryRequestHash(request);
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        if (request.ClientMessageId is { } requestId)
        {
            var replay = await c.QuerySingleOrDefaultAsync<MessageReplay>(new CommandDefinition(
                SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/SupportRepository/GetMessageForActorAndRequest",
                ("ThreadScope", ThreadScope)),
                new { ActorId = actor, Request = requestId },
                tx,
                cancellationToken: ct));
            if (replay is not null)
            {
                if (enquiryHash is not null)
                {
                    if (enquiryHash != replay.EnquiryRequestHash)
                    {
                        throw new PortalConflictException("This enquiry request was already used for different answers.");
                    }

                    await tx.CommitAsync(ct);
                    return replay.ThreadId;
                }

                if ((request.ConcernCode is not null
                    && request.ConcernCode != replay.ConcernCode)
                    || (request.ThreadId.HasValue
                    && request.ThreadId != replay.ThreadId)
                    || (request.DisputeId.HasValue
                    && request.DisputeId != replay.DisputeId)
                    || (request.TransactionId.HasValue
                    && request.TransactionId != replay.TransactionId)
                    || (request.RelatedDisputeId.HasValue
                    && request.RelatedDisputeId != replay.RelatedDisputeId)
                    || (!request.ThreadId.HasValue
                    && !request.DisputeId.HasValue
                    && !request.TransactionId.HasValue
                    && !request.RelatedDisputeId.HasValue
                    && (replay.DisputeId.HasValue
                    || replay.TransactionId.HasValue
                    || replay.RelatedDisputeId.HasValue))
                    || request.Body.Trim() != replay.Body
                    || request.Subject.Trim() != replay.Subject)
                {
                    throw new PortalConflictException("This message request was used for different content.");
                }

                EnsureSupportOpen(replay.Status);
                await EnsureConversationOpen(c, tx, replay.DisputeId, replay.TransactionId, replay.RelatedDisputeId, ct);
                await tx.CommitAsync(ct);
                return replay.ThreadId;
            }
        }

        string? enquiryJson = null;
        if (request.Enquiry is not null)
        {
            var prepared = await PrepareGuidedEnquiry(c, tx, actor, request, ct);
            request = prepared.Request;
            enquiryJson = JsonSerializer.Serialize(prepared.Snapshot, JsonSerializerOptions.Web);
        }

        SupportThreadDto? thread;
        if (request.ThreadId is { } tid)
        {
            thread = await c.QuerySingleOrDefaultAsync<SupportThreadDto>(new CommandDefinition(
                SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/SupportRepository/GetConversationWithinActorReadScope",
                ("ThreadSelect", ThreadSelect),
                ("ThreadScope", ThreadScope)),
                new { Id = tid, ActorId = actor },
                tx,
                cancellationToken: ct)) ?? throw new PortalNotFoundException();
            if ((request.TransactionId.HasValue
                && request.TransactionId != thread.TransactionId)
                || (request.DisputeId.HasValue
                && request.DisputeId != thread.DisputeId)
                || (request.RelatedDisputeId.HasValue
                && request.RelatedDisputeId != thread.RelatedDisputeId))
            {
                throw new PortalConflictException("This conversation belongs to a different transaction or case.");
            }
        }
        else if (request.RelatedDisputeId is { } relatedCaseId)
        {
            var target = await c.QuerySingleOrDefaultAsync<RelatedSupportTarget>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/SupportRepository/GetActiveDisputeForActor"),
                new { Id = relatedCaseId, Actor = actor },
                tx,
                cancellationToken: ct)) ?? throw new PortalNotFoundException();
            if (DisputeWorkflow.CanSendCaseMessage(target.Status))
            {
                throw new PortalConflictException("Use case messages while your dispute is open.");
            }

            thread = request.StartNewConversation ? null : await c.QuerySingleOrDefaultAsync<SupportThreadDto>(new CommandDefinition(
                SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/SupportRepository/GetLatestOpenCustomerFollowUpForCase",
                ("ThreadSelect", ThreadSelect)),
                new { ActorId = actor, Related = relatedCaseId },
                tx,
                cancellationToken: ct));
            thread ??= new SupportThreadDto
            {
                Id = Guid.NewGuid(),
                CustomerId = actor,
                RelatedDisputeId = relatedCaseId,
                RelatedCaseContext = RelatedContext(target),
                Subject = request.Subject.Trim()
            };
        }
        else
        {
            Guid? transactionId = request.TransactionId, disputeId = request.DisputeId;
            var owner = actor;
            if (disputeId is { } did)
            {
                var target = await c.QuerySingleOrDefaultAsync<SupportTarget>(new CommandDefinition(
                    SqlScripts.RenderSqlScript(
                    "Shared/Queries/Api/SupportRepository/GetDisputeConversationRecipientContext",
                    ("StaffScopeSql", StaffScopeSql)),
                    new { Id = did, ActorId = actor },
                    tx,
                    cancellationToken: ct)) ?? throw new PortalNotFoundException();
                if (transactionId.HasValue && transactionId != target.TransactionId)
                {
                    throw new PortalConflictException("Select the transaction linked to this case.");
                }

                transactionId = target.TransactionId;
                owner = target.CustomerId;
            }
            else if (transactionId is { } targetId)
            {
                var target = await c.QuerySingleOrDefaultAsync<SupportTarget>(new CommandDefinition(
                    SqlScripts.ReadSqlScript("Shared/Queries/Api/SupportRepository/GetTransactionConversationRecipientContext"),
                    new { Id = targetId, ActorId = actor },
                    tx,
                    cancellationToken: ct)) ?? throw new PortalNotFoundException();
                disputeId = target.DisputeId;
                owner = target.CustomerId;
            }
            else if (!(await Roles(c, tx, actor, ct)).Contains("customer"))
            {
                throw new PortalForbiddenException("Select a customer conversation to reply.");
            }

            thread = transactionId.HasValue ? await c.QuerySingleOrDefaultAsync<SupportThreadDto>(new CommandDefinition(
                SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/SupportRepository/GetLatestOpenConversationForTransactionOrCase",
                ("ThreadSelect", ThreadSelect)),
                new
                {
                    Owner = owner,
                    Transaction = transactionId,
                    Dispute = disputeId,
                    ActorId = actor
                },
                tx,
                cancellationToken: ct)) : null;
            thread ??= new SupportThreadDto
            {
                Id = Guid.NewGuid(),
                CustomerId = owner,
                DisputeId = disputeId,
                TransactionId = transactionId,
                Subject = request.Subject.Trim()
            };
            if (thread.DisputeId is null && disputeId.HasValue)
            {
                await c.ExecuteAsync(new CommandDefinition(
                    SqlScripts.ReadSqlScript("Shared/Queries/Api/SupportRepository/UpdateConversationsForDispute"),
                    new { Dispute = disputeId, thread.Id },
                    tx,
                    cancellationToken: ct));
                thread.DisputeId = disputeId;
            }
        }

        EnsureSupportOpen(thread.Status);
        // A related enquiry is general support. A case conversation retains the
        // status of its exact case, even after a later reinstatement exists.
        await EnsureConversationOpen(c, tx, thread.DisputeId, thread.TransactionId, thread.RelatedDisputeId, ct);
        var newConversation = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT
                COUNT(*)
            FROM "support"."conversations" AS "support_threads"
            WHERE "support_threads"."publicId" = @Id
            """,
            new { thread.Id },
            tx,
            cancellationToken: ct)) == 0;
        if (thread.CustomerId != actor)
        {
            var access = await Capabilities(c, tx, actor, ct);
            if (access.Reply != 1)
            {
                throw new PortalForbiddenException();
            }

            if (newConversation && thread.DisputeId is { } workCase)
            {
                await EnsureCanWork(c, tx, actor, workCase, ct);
            }
            else if (access.Supervisor != 1 && thread.AssignedTo != actor)
            {
                throw new PortalForbiddenException("Only the assigned, qualified staff member can reply.");
            }
        }

        if (newConversation)
        {
            var concern = await GetConversationConcern(c, tx, thread, request.ConcernCode, ct);
            await c.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/SupportRepository/InsertConversation"),
                new
                {
                    thread.Id,
                    thread.CustomerId,
                    thread.DisputeId,
                    thread.TransactionId,
                    thread.Subject,
                    ConcernId = concern.Id
                },
                tx,
                cancellationToken: ct));
            if (thread.RelatedDisputeId is { } newRelatedId)
            {
                await c.ExecuteAsync(new CommandDefinition(
                    SqlScripts.ReadSqlScript("Shared/Queries/Api/SupportRepository/UpdateConversationsWithRelatedDisputeId"),
                    new
                    {
                        thread.Id,
                        Related = newRelatedId,
                        Context = thread.RelatedCaseContext
                    },
                    tx,
                    cancellationToken: ct));
            }

            await PrepareConversation(c, tx, thread, concern, ct);
        }

        var messageId = Guid.NewGuid();
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/SupportRepository/InsertMessage",
            ("AuditMetadataValueExpression", dialect.AuditMetadataValueExpression("@Enquiry"))),
            new
            {
                Id = messageId,
                Thread = thread.Id,
                Actor = actor,
                Body = request.Body.Trim(),
                request.ClientMessageId,
                RequestSubject = request.Subject.Trim(),
                Enquiry = enquiryJson,
                EnquiryHash = enquiryHash
            },
            tx,
            cancellationToken: ct));
        if (thread.DisputeId is { } linked)
        {
            await c.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/SupportRepository/InsertNote"),
                new
                {
                    Id = Guid.NewGuid(),
                    Dispute = linked,
                    Actor = actor,
                    Body = request.Body.Trim()
                },
                tx,
                cancellationToken: ct));
            if (actor != thread.CustomerId)
            {
                await c.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE "disputes"."disputes"
                    SET
                        "firstResponseAt" = COALESCE("disputes"."firstResponseAt", @Now)
                    WHERE "disputes"."publicId" = @Id
                    """,
                    new { Id = linked, Now = DateTimeOffset.UtcNow },
                    tx,
                    cancellationToken: ct));
            }
        }

        var recipients = new List<Guid>();
        if (actor != thread.CustomerId)
        {
            recipients.Add(thread.CustomerId);
        }
        else if (thread.DisputeId is { } linkedId)
        {
            var receiver = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/SupportRepository/GetLinkedCaseOwnerForSupportReply"),
                new { Id = linkedId },
                tx,
                cancellationToken: ct));
            if (receiver is { } staff)
            {
                recipients.Add(staff);
            }
        }
        else if (thread.AssignedTo is { } assigned)
        {
            recipients.Add(assigned);
        }
        else
        {
            recipients.AddRange(await Supervisors(c, tx, actor, ct));
        }

        await DisputeNotificationOutbox.EnqueueEvent(
            c,
            tx,
            null,
            messageId,
            "SupportReply",
            "New support message",
            "A new message is available in your secure support conversation.",
            recipients,
            ct,
            "/support?thread=" + thread.Id,
            thread.Id);
        if (enquiryHash is not null)
        {
            await AuditOperation(
                c,
                tx,
                actor,
                "SupportEnquirySubmitted",
                thread.Id,
                new
                {
                    request.ConcernCode,
                    thread.DisputeId,
                    thread.TransactionId,
                    thread.RelatedDisputeId,
                    thread.QueueType,
                    thread.EscalationLevel,
                    thread.AssignedTo
                },
                ct);
        }

        await AuditOperation(
            c,
            tx,
            actor,
            "SupportMessageSent",
            thread.Id,
            new
            {
                thread.DisputeId,
                thread.TransactionId,
                thread.RelatedDisputeId
            },
            ct);
        await tx.CommitAsync(ct);
        return thread.Id;
    }

    private static string RelatedContext(RelatedSupportTarget target)
    {
        var merchant = string.IsNullOrWhiteSpace(target.MerchantName) ? target.Description : target.MerchantName;
        var context = FormattableString.Invariant($"Case {target.Reference} · {merchant} · {target.Amount:0.00} {target.Currency} · {target.Status} · Submitted {target.CreatedAt:yyyy-MM-dd}");
        return context.Length <= 1000 ? context : context[..1000];
    }

    public async Task<DisputePortal.Api.Models.Responses.SupportThreadDto?> GetSupportThread(Guid actor, Guid thread, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        var item = await c.QuerySingleOrDefaultAsync<SupportThreadDto>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/SupportRepository/GetThreadWithinActorReadScope",
            ("ThreadSelect", ThreadSelect),
            ("ThreadScope", ThreadScope)),
            new { Thread = thread, ActorId = actor },
            cancellationToken: ct));
        if (item is not null)
        {
            await Decorate(c, null, actor, [item], ct);
        }

        return item;
    }

    public async Task<Guid?> GetLastSupportThread(Guid actor, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        return await c.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            SqlScripts.RenderSqlScript("Shared/Queries/Api/SupportRepository/GetReadStateForActor", ("ThreadScope", ThreadScope)),
            new { ActorId = actor },
            cancellationToken: ct));
    }

    public async Task MarkSupportRead(Guid actor, Guid thread, Guid message, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        var target = await c.QuerySingleOrDefaultAsync<ReadReceiptTarget>(new CommandDefinition(
            SqlScripts.RenderSqlScript("Shared/Queries/Api/SupportRepository/GetMessage", ("ThreadScope", ThreadScope)),
            new
            {
                Message = message,
                Thread = thread,
                ActorId = actor
            },
            tx,
            cancellationToken: ct)) ?? throw new PortalNotFoundException();
        var args = new
        {
            User = target.UserId,
            Thread = target.ConversationId,
            target.Sequence,
            Now = DateTimeOffset.UtcNow
        };
        if (await c.ExecuteAsync(new CommandDefinition(
            """
            UPDATE support."readStates"
            SET
                "lastReadSequence" = CASE WHEN "lastReadSequence" > @Sequence THEN "lastReadSequence" ELSE @Sequence END,
                "openedAt" = @Now
            WHERE "userId" = @User
            AND "conversationId" = @Thread
            """,
            args,
            tx,
            cancellationToken: ct)) == 0)
        {
            await c.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO support."readStates"(
                    "userId",
                    "conversationId",
                    "lastReadSequence",
                    "openedAt"
                )
                VALUES (
                    @User,
                    @Thread,
                    @Sequence,
                    @Now
                )
                """,
                args,
                tx,
                cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
    }

    private class RelatedSupportTarget
    {
        public Guid Id { get; set; }
        public string Reference { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string? MerchantName { get; set; }
        public string Description { get; set; } = string.Empty;

        public RelatedSupportTarget()
        {
        }

        public RelatedSupportTarget(
            Guid Id,
            string Reference,
            string Status,
            DateTimeOffset CreatedAt,
            decimal Amount,
            string Currency,
            string? MerchantName,
            string Description)
        {
            this.Id = Id;
            this.Reference = Reference;
            this.Status = Status;
            this.CreatedAt = CreatedAt;
            this.Amount = Amount;
            this.Currency = Currency;
            this.MerchantName = MerchantName;
            this.Description = Description;
        }
    }

    private class MessageReplay
    {
        public string Status { get; set; } = "";
        public Guid ThreadId { get; set; }
        public string Body { get; set; } = "";
        public Guid? DisputeId { get; set; }
        public Guid? TransactionId { get; set; }
        public Guid? RelatedDisputeId { get; set; }
        public string Subject { get; set; } = "";
        public string ConcernCode { get; set; } = "";
        public string? EnquiryRequestHash { get; set; }
    }

    private class SupportTarget
    {
        public Guid CustomerId { get; set; }
        public Guid TransactionId { get; set; }
        public Guid? DisputeId { get; set; }

        public SupportTarget()
        {
        }

        public SupportTarget(Guid CustomerId, Guid TransactionId, Guid? DisputeId)
        {
            this.CustomerId = CustomerId;
            this.TransactionId = TransactionId;
            this.DisputeId = DisputeId;
        }
    }

    private class ReadReceiptTarget
    {
        public long Sequence { get; set; }
        public long UserId { get; set; }
        public long ConversationId { get; set; }

        public ReadReceiptTarget()
        {
        }

        public ReadReceiptTarget(long Sequence, long UserId, long ConversationId)
        {
            this.Sequence = Sequence;
            this.UserId = UserId;
            this.ConversationId = ConversationId;
        }
    }
}
