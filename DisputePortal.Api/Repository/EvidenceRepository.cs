using Dapper;
using DisputePortal.Api.Configuration;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;
using System.Data.Common;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Security;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;

public class EvidenceRepository(
    IDatabaseConnectionFactory factory,
    IDatabaseDialect databaseDialect,
    ISecretProtector secretProtector,
    IEvidencePolicyRepository policyRepository,
    ApiRuntimeOptions runtime,
    ILogger<EvidenceRepository>? logger = null) : RepositoryBase(factory, databaseDialect), IEvidenceRepository
{


    private async Task EnsureEvidenceAccess(DbConnection c, DbTransaction? tx, Guid actor, Guid disputeId, bool write, CancellationToken ct)
    {
        var row = await c.QuerySingleOrDefaultAsync<EvidenceCase>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/EvidenceRepository/GetEvidenceCaseAccessContext",
            ("StaffScopeSql", StaffScopeSql)),
            new { Id = disputeId, ActorId = actor },
            tx,
            cancellationToken: ct)) ?? throw new PortalNotFoundException();
        if (write)
        {
            if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT
                    COUNT(*)
                FROM "identity"."users" AS "users"
                WHERE "users"."publicId" = @ActorId
                AND "users"."accountStatus" = 'Active'
                """,
                new { ActorId = actor },
                tx,
                cancellationToken: ct)) != 1)
            {
                throw new PortalForbiddenException("This account cannot add evidence.");
            }

            if (!DisputeWorkflow.CanSendCaseMessage(row.Status))
            {
                throw new PortalConflictException("This case is closed. Evidence can only be added to an active case.");
            }

            if (row.CustomerId != actor)
            {
                if (tx is null)
                {
                    var roles = await Roles(c, null, actor, ct);
                    if (!roles.Any(CaseServicePolicy.IsSupervisor) && !roles.Any(CaseServicePolicy.IsSpecialist))
                    {
                        throw new PortalForbiddenException();
                    }
                }
                else
                {
                    await EnsureCanWork(c, tx, actor, disputeId, ct);
                }
            }
        }
    }

    public async Task EnsureEvidenceUploadAllowed(Guid actor, Guid disputeId, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        await EnsureEvidenceAccess(c, tx, actor, disputeId, true, ct);
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<EvidenceDto>> GetEvidence(Guid actor, Guid disputeId, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await EnsureEvidenceAccess(c, null, actor, disputeId, false, ct);
        var rows = (await c.QueryAsync<EvidenceDto>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/EvidenceRepository/GetCaseEvidenceIncludingRemovalAudit"),
            new { Id = disputeId },
            cancellationToken: ct))).AsList();
        var access = await ReadEvidenceCase(c, null, disputeId, ct);
        var roles = await Roles(c, null, actor, ct);
        var active = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT
                COUNT(*)
            FROM "identity"."users" AS "users"
            WHERE "users"."publicId" = @Actor
            AND "users"."accountStatus" = 'Active'
            """,
            new { Actor = actor },
            cancellationToken: ct)) == 1;
        var supervisor = active && roles.Any(CaseServicePolicy.IsSupervisor);
        var owner = active && (access.CustomerId == actor || roles.Contains("super_admin"));
        var eligible = active
            && (supervisor
            || (access.AssignedTo == actor
            && roles.Any(role => CaseServicePolicy.CanWork(role, access.QueueType))));
        var granted = new HashSet<Guid>();
        if (!owner && eligible)
        {
            granted = (await c.QueryAsync<Guid>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/EvidenceRepository/GetEvidenceForActorAndDispute"),
                new
                {
                    Case = disputeId,
                    Actor = actor,
                    Now = DateTimeOffset.UtcNow
                },
                cancellationToken: ct))).ToHashSet();
        }

        var removalActors = await ActorIdentityReader.GetRecordedActorIdentities(
            c,
            null,
            dialect,
            "Audit",
            rows.Where(row => row.RemovalAuditId.HasValue).Select(row => row.RemovalAuditId!.Value),
            supervisor,
            ct);
        foreach (var row in rows)
        {
            var visibleMetadata = owner || granted.Contains(row.Id);
            row.CanManageAccess = supervisor && row.DeletedAt is null;
            row.CanView = visibleMetadata && row.DeletedAt is null;
            row.CanRemoveOwn = active && row.UploadedById == actor && row.DeletedAt is null;
            row.CanRemove = active && (row.UploadedById == actor || supervisor) && row.DeletedAt is null;
            if (row.RemovalAuditId is { } auditId && removalActors.TryGetValue(auditId, out var removed))
            {
                row.DeletedBy = removed.Label;
            }

            if (!visibleMetadata)
            {
                row.FileName = "Restricted document";
                row.ContentType = "";
                row.SizeBytes = 0;
                row.UploadedBy = "";
                row.ScanStatus = "";
                row.HasOriginal = false;
                row.OriginalFileName = null;
            }
        }

        return rows;
    }

    public async Task<Guid> AddEvidence(
        Guid actor,
        Guid disputeId,
        string fileName,
        byte[] content,
        string scanStatus,
        string category,
        CancellationToken ct,
        EvidenceOriginal? original = null)
    {
        var policy = await policyRepository.GetEvidencePolicy(ct);
        if (!policy.Categories.Any(option => option.Code == category))
        {
            throw new PortalValidationException("Choose a document category.");
        }

        EvidenceFileDescription file;
        try
        {
            file = EvidenceFilePolicy.Inspect(fileName, content, policy);
        }
        catch (ArgumentException e)
        {
            throw new PortalValidationException(e.Message);
        }

        EvidenceFileDescription? originalInfo = null;
        if (original is not null)
        {
            try
            {
                originalInfo = EvidenceFilePolicy.Inspect(original.FileName, original.Content, policy, originalFile: true);
            }
            catch (ArgumentException e)
            {
                throw new PortalValidationException(e.Message);
            }

            if (!originalInfo.ContentType.StartsWith("image/", StringComparison.Ordinal)
                || !file.ContentType.StartsWith("image/", StringComparison.Ordinal))
            {
                throw new PortalValidationException("Only image optimisations may retain an original photo.");
            }
        }

        if (scanStatus is not ("Validated" or "Scanned" or "Unscanned"))
        {
            throw new PortalValidationException("The file safety result is invalid.");
        }

        await EnsureEvidenceUploadAllowed(actor, disputeId, ct);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        // Encrypt before acquiring the shared business lock; large photographs must not hold it during CPU work.
        var id = Guid.NewGuid();
        var protectedContent = secretProtector.ProtectBytes(content, EvidenceContext(disputeId, id, false));
        var protectedOriginal = original is null ? null : secretProtector.ProtectBytes(
            original.Content,
            EvidenceContext(disputeId, id, true));
        logger?.LogInformation(
            "Evidence payload prepared {DisputeId} {PrimaryBytes} {OriginalBytes} in {ElapsedMs} ms",
            disputeId,
            content.Length,
            original?.Content.Length ?? 0,
            elapsed.ElapsedMilliseconds);
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        logger?.LogInformation("Evidence payload transaction opened {EvidenceId} in {ElapsedMs} ms", id, elapsed.ElapsedMilliseconds);
        // The private payload has no case foreign key. Stream it before taking business locks;
        // metadata, quota, authorisation and audit still commit atomically with these bytes.
        try
        {
            await EvidencePayloadStorage.InsertEncryptedEvidencePayload(c, tx, id, protectedContent, protectedOriginal, runtime.EvidencePayloadWriteTimeoutSeconds, ct);
        }
        catch (Microsoft.Data.SqlClient.SqlException failure)
        {
            logger?.LogError("Evidence storage failed {EvidenceId} SQL code {SqlCode} state {SqlState}", id, failure.Number, failure.State);
            throw;
        }

        logger?.LogInformation("Evidence payload stored {EvidenceId} in {ElapsedMs} ms", id, elapsed.ElapsedMilliseconds);
        await LockOperations(c, tx, ct);
        await EnsureEvidenceAccess(c, tx, actor, disputeId, true, ct);
        var existing = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/EvidenceRepository/FindDuplicateEvidenceByCaseAndHash"),
            new
            {
                Id = disputeId,
                Hash = file.Sha256,
                OriginalHash = originalInfo?.Sha256
            },
            tx,
            cancellationToken: ct));
        if (existing is { } duplicate)
        {
            var previousCategory = await c.ExecuteScalarAsync<string>(new CommandDefinition(
                """
                SELECT
                    "dispute_evidence"."category" AS "category"
                FROM "disputes"."evidence" AS "dispute_evidence"
                WHERE "dispute_evidence"."publicId" = @Id
                """,
                new { Id = duplicate },
                tx,
                cancellationToken: ct));
            if (previousCategory != category)
            {
                throw new PortalConflictException("This file is already attached in a different category. Review the existing evidence.");
            }

            await tx.RollbackAsync(ct);
            return duplicate;
        }

        var count = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/EvidenceRepository/CountEvidence"),
            new { Id = disputeId },
            tx,
            cancellationToken: ct));
        var total = await c.ExecuteScalarAsync<long>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/EvidenceRepository/SumCaseEvidenceStorageBytes"),
            new { Id = disputeId },
            tx,
            cancellationToken: ct));
        if (count >= policy.MaximumFiles
            || total + content.Length + (original?.Content.Length ?? 0) > policy.MaximumCaseBytes)
        {
            throw new PortalConflictException("This case has reached its configured evidence storage limit.");
        }

        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/EvidenceRepository/InsertEvidence"),
            new
            {
                Id = id,
                Case = disputeId,
                Actor = actor,
                Name = file.FileName,
                Type = file.ContentType,
                Size = content.Length,
                Hash = file.Sha256,
                Scan = scanStatus,
                Category = category,
                Now = DateTimeOffset.UtcNow
            },
            tx,
            cancellationToken: ct));
        logger?.LogInformation("Evidence metadata stored {EvidenceId} in {ElapsedMs} ms", id, elapsed.ElapsedMilliseconds);
        if (original is not null && originalInfo is not null)
        {
            await c.ExecuteAsync(new CommandDefinition(
                """
                UPDATE "disputes"."evidence"
                SET
                    "originalFileName" = @Name,
                    "originalContentType" = @Type,
                    "originalSizeBytes" = @Size,
                    "originalSha256" = @Hash,
                    "protectedOriginal" = 'binary:v1'
                WHERE "evidence"."publicId" = @Id
                """,
                new
                {
                    Id = id,
                    Name = originalInfo.FileName,
                    Type = originalInfo.ContentType,
                    Size = original.Content.Length,
                    Hash = originalInfo.Sha256
                },
                tx,
                cancellationToken: ct));
        }

        logger?.LogInformation("Evidence original metadata stored {EvidenceId} in {ElapsedMs} ms", id, elapsed.ElapsedMilliseconds);
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/EvidenceRepository/InsertNote"),
            new
            {
                Id = Guid.NewGuid(),
                Case = disputeId,
                Actor = actor
            },
            tx,
            cancellationToken: ct));
        await AuditOperation(
            c,
            tx,
            actor,
            "EvidenceAttached",
            disputeId,
            new
            {
                EvidenceId = id,
                Category = category,
                SizeBytes = content.Length,
                ContentType = file.ContentType,
                ScanStatus = scanStatus,
                OriginalSha256 = originalInfo?.Sha256,
                OriginalSize = original?.Content.Length,
                OptimizedSha256 = file.Sha256
            },
            ct);
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task RemoveEvidence(Guid actor, Guid disputeId, Guid evidenceId, RemoveEvidenceRequest request, CancellationToken ct)
    {
        await ValidateEvidenceAccessReason(request.Reason, ct);
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        await EnsureEvidenceAccess(c, tx, actor, disputeId, false, ct);
        var row = await c.QuerySingleOrDefaultAsync<RemovalRow>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/EvidenceRepository/GetEvidenceForDisputeWithUploadedByAndDeletedAt"),
            new { Evidence = evidenceId, Case = disputeId },
            tx,
            cancellationToken: ct)) ?? throw new PortalNotFoundException();
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT
                COUNT(*)
            FROM "identity".users
            WHERE "publicId" = @Actor
            AND "accountStatus" = 'Active'
            AND "deletedAt" IS NULL
            """,
            new { Actor = actor },
            tx,
            cancellationToken: ct)) != 1)
        {
            throw new PortalForbiddenException();
        }

        if (row.UploadedBy != actor && !(await Roles(c, tx, actor, ct)).Any(CaseServicePolicy.IsSupervisor))
        {
            throw new PortalForbiddenException("Only the uploader or an operations supervisor can remove this file.");
        }

        if (row.DeletedAt is not null)
        {
            await tx.CommitAsync(ct);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/EvidenceRepository/SoftDeleteEvidenceForActor"),
            new
            {
                Evidence = evidenceId,
                Actor = actor,
                Now = now,
                Reason = secretProtector.Protect(request.Reason.Trim())
            },
            tx,
            cancellationToken: ct));
        // The native removal trigger writes RecordSoftDeleted and captures the
        // actor snapshot in this same transaction; audit failure rolls it all back.
        await tx.CommitAsync(ct);
    }

    public Task<EvidenceDownload> DownloadEvidence(Guid actor, Guid disputeId, Guid evidenceId, CancellationToken ct) => ReadEvidence(
        actor,
        disputeId,
        evidenceId,
        false,
        ct);

    public Task<EvidenceDownload> DownloadOriginalEvidence(Guid actor, Guid disputeId, Guid evidenceId, CancellationToken ct) =>
        ReadEvidence(actor, disputeId, evidenceId, false, ct, true);

    public Task<EvidenceDownload> PreviewEvidence(Guid actor, Guid disputeId, Guid evidenceId, CancellationToken ct) => ReadEvidence(
        actor,
        disputeId,
        evidenceId,
        true,
        ct);

    private static string EvidenceContext(Guid disputeId, Guid evidenceId, bool original) =>
        $"Evidence/{disputeId:N}/{evidenceId:N}/{(original ? "Original" : "Optimized")}";

    private async Task RequireReadableEvidence(
        DbConnection c,
        DbTransaction tx,
        Guid actor,
        Guid disputeId,
        Guid evidenceId,
        CancellationToken ct)
    {
        await EnsureEvidenceAccess(c, tx, actor, disputeId, false, ct);
        await RequireEvidenceExists(c, tx, disputeId, evidenceId, ct);
        if (!await CanReadEvidence(c, tx, actor, disputeId, evidenceId, ct))
        {
            throw new PortalForbiddenException("Ask an operations supervisor for access to this document.");
        }
    }

    private async Task<EvidenceDownload> ReadEvidence(
        Guid actor,
        Guid disputeId,
        Guid evidenceId,
        bool preview,
        CancellationToken ct,
        bool original = false)
    {
        await using var c = await connections.OpenConnection(ct);
        await using (var tx = await c.BeginTransactionAsync(ct))
        {
            await LockOperations(c, tx, ct);
            await RequireReadableEvidence(c, tx, actor, disputeId, evidenceId, ct);
            await AuditOperation(
                c,
                tx,
                actor,
                original ? "EvidenceOriginalDownloadRequested" : preview ? "EvidencePreviewRequested" : "EvidenceDownloadRequested",
                disputeId,
                new { EvidenceId = evidenceId },
                ct);
            await tx.CommitAsync(ct);
        }

        // Transfer/decrypt the large immutable payload without holding the application-wide lock.
        var query = original ? SqlScripts.ReadSqlScript("Shared/Queries/Api/EvidenceRepository/GetOriginalEncryptedEvidencePayload") : SqlScripts.ReadSqlScript("Shared/Queries/Api/EvidenceRepository/GetOptimizedEncryptedEvidencePayload");
        var file = await c.QuerySingleOrDefaultAsync<EvidenceContentRow>(new CommandDefinition(
            query,
            new { Id = evidenceId, Case = disputeId },
            cancellationToken: ct)) ?? throw new PortalNotFoundException();
        if (file.ProtectedBytes is null && string.IsNullOrEmpty(file.ProtectedContent))
        {
            throw new PortalNotFoundException("No original photo was retained for this file.");
        }

        var content = file.ProtectedBytes is not null ? secretProtector.UnprotectBytes(
            file.ProtectedBytes,
            EvidenceContext(disputeId, evidenceId, original)) : Convert.FromBase64String(secretProtector.Unprotect(file.ProtectedContent));
        // A grant or assignment may change during a slow transfer; recheck before returning bytes.
        await using (var verify = await c.BeginTransactionAsync(ct))
        {
            await LockOperations(c, verify, ct);
            await RequireReadableEvidence(c, verify, actor, disputeId, evidenceId, ct);
            await verify.CommitAsync(ct);
        }

        return new(file.FileName, file.ContentType, content, file.ScanStatus);
    }

    private async Task<EvidenceCaseAccess> ReadEvidenceCase(DbConnection c, DbTransaction? tx, Guid disputeId, CancellationToken ct) =>
        await c.QuerySingleOrDefaultAsync<EvidenceCaseAccess>(new CommandDefinition(
        SqlScripts.ReadSqlScript("Shared/Queries/Api/EvidenceRepository/GetEvidenceCaseOwnershipAndAssignment"),
        new { Id = disputeId },
        tx,
        cancellationToken: ct)) ?? throw new PortalNotFoundException();

    private static async Task RequireEvidenceExists(
        DbConnection c,
        DbTransaction? tx,
        Guid disputeId,
        Guid evidenceId,
        CancellationToken ct)
    {
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/EvidenceRepository/CountEvidenceForDispute"),
            new { Id = evidenceId, Case = disputeId },
            tx,
            cancellationToken: ct)) != 1)
        {
            throw new PortalNotFoundException();
        }
    }

    private async Task<bool> EligibleEvidenceRecipient(
        DbConnection c,
        DbTransaction? tx,
        Guid userId,
        EvidenceCaseAccess row,
        CancellationToken ct)
    {
        var roles = await Roles(c, tx, userId, ct);
        return roles.Any(CaseServicePolicy.IsSupervisor)
            || (row.AssignedTo == userId
            && roles.Any(role => CaseServicePolicy.CanWork(role, row.QueueType)));
    }

    private async Task<bool> CanReadEvidence(
        DbConnection c,
        DbTransaction? tx,
        Guid actor,
        Guid disputeId,
        Guid evidenceId,
        CancellationToken ct)
    {
        var row = await ReadEvidenceCase(c, tx, disputeId, ct);
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT
                COUNT(*)
            FROM "identity"."users" AS "users"
            WHERE "users"."publicId" = @Id
            AND "users"."accountStatus" = 'Active'
            """,
            new { Id = actor },
            tx,
            cancellationToken: ct)) != 1)
        {
            return false;
        }

        if (row.CustomerId == actor)
        {
            return true;
        }

        // Explicit privileged access: every preview/download still records an audit and passes in-process validation.
        if ((await Roles(c, tx, actor, ct)).Contains("super_admin"))
        {
            return true;
        }

        if (!await EligibleEvidenceRecipient(c, tx, actor, row, ct))
        {
            return false;
        }

        return await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/EvidenceRepository/CountEvidenceAccessGrantsForActor"),
            new
            {
                Id = evidenceId,
                Actor = actor,
                Now = DateTimeOffset.UtcNow
            },
            tx,
            cancellationToken: ct)) > 0;
    }

    public async Task<EvidenceAccessDto> GetEvidenceAccess(Guid actor, Guid disputeId, Guid evidenceId, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await RequireSupervisor(c, null, actor, ct);
        await RequireEvidenceExists(c, null, disputeId, evidenceId, ct);
        var row = await ReadEvidenceCase(c, null, disputeId, ct);
        var grants = (await c.QueryAsync<EvidenceGrantDto>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/EvidenceRepository/GetEvidenceAccessGrants"),
            new { Id = evidenceId },
            cancellationToken: ct))).AsList();
        var recipients = (await c.QueryAsync<EvidenceRecipientDto>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/EvidenceRepository/GetActiveUsersForCustomerAndActor"),
            new
            {
                Actor = actor,
                Customer = row.CustomerId,
                Assigned = row.AssignedTo,
                Specialist = CaseServicePolicy.SpecialistFor(row.QueueType)
            },
            cancellationToken: ct))).AsList();
        return new(grants, recipients);
    }

    public async Task GrantEvidenceAccess(
        Guid actor,
        Guid disputeId,
        Guid evidenceId,
        GrantEvidenceAccessRequest request,
        CancellationToken ct)
    {
        var policy = await ValidateEvidenceAccessReason(request.Reason, ct);
        if (request.Hours < policy.MinimumAccessHours || request.Hours > policy.MaximumAccessHours)
        {
            throw new PortalValidationException("Choose an access period within the configured range.");
        }

        if (request.UserId == actor)
        {
            throw new PortalForbiddenException("Another supervisor must approve your document access.");
        }

        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        await RequireSupervisor(c, tx, actor, ct);
        await RequireEvidenceExists(c, tx, disputeId, evidenceId, ct);
        var row = await ReadEvidenceCase(c, tx, disputeId, ct);
        if (request.UserId == row.CustomerId || !await EligibleEvidenceRecipient(c, tx, request.UserId, row, ct))
        {
            throw new PortalForbiddenException("Choose an active supervisor or the qualified specialist assigned to this case.");
        }

        var now = DateTimeOffset.UtcNow;
        var expires = now.AddHours(request.Hours);
        // Preserve expired/replaced grants as an audit trail instead of extending them silently.
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/EvidenceRepository/RevokeEvidenceAccessGrantsForUserAndActor"),
            new
            {
                Id = Guid.NewGuid(),
                Evidence = evidenceId,
                User = request.UserId,
                Actor = actor,
                Reason = secretProtector.Protect(request.Reason.Trim()),
                Replacement = secretProtector.Protect("Replaced by a new explicit grant."),
                Now = now,
                Expires = expires
            },
            tx,
            cancellationToken: ct));
        await AuditOperation(
            c,
            tx,
            actor,
            "EvidenceAccessGranted",
            disputeId,
            new
            {
                EvidenceId = evidenceId,
                RecipientId = request.UserId,
                ExpiresAt = expires
            },
            ct);
        await tx.CommitAsync(ct);
    }

    public async Task RevokeEvidenceAccess(
        Guid actor,
        Guid disputeId,
        Guid evidenceId,
        Guid grantId,
        RevokeEvidenceAccessRequest request,
        CancellationToken ct)
    {
        await ValidateEvidenceAccessReason(request.Reason, ct);
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        await RequireSupervisor(c, tx, actor, ct);
        await RequireEvidenceExists(c, tx, disputeId, evidenceId, ct);
        var count = await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/EvidenceRepository/RevokeEvidenceAccessGrantsForActor"),
            new
            {
                Id = grantId,
                Evidence = evidenceId,
                Actor = actor,
                Now = DateTimeOffset.UtcNow,
                Reason = secretProtector.Protect(request.Reason.Trim())
            },
            tx,
            cancellationToken: ct));
        if (count != 1)
        {
            throw new PortalConflictException("This access grant is no longer active. Refresh the document permissions.");
        }

        await AuditOperation(c, tx, actor, "EvidenceAccessRevoked", disputeId, new { EvidenceId = evidenceId, GrantId = grantId }, ct);
        await tx.CommitAsync(ct);
    }

    private async Task<DisputePortal.Api.Models.Configuration.EvidencePolicy> ValidateEvidenceAccessReason(string? reason, CancellationToken ct)
    {
        var policy = await policyRepository.GetEvidencePolicy(ct);
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < policy.AccessReasonMinimumLength || reason.Trim().Length > policy.AccessReasonMaximumLength)
        {
            throw new PortalValidationException("Add an access reason within the configured length limits.");
        }

        return policy;
    }

    private class EvidenceCase
    {
        public Guid CustomerId { get; set; }
        public string Status { get; set; } = "";
    }

    private class EvidenceContentRow
    {
        public string FileName { get; set; } = "";
        public string ContentType { get; set; } = "";
        public string ProtectedContent { get; set; } = "";
        public string ScanStatus { get; set; } = "";
        public byte[]? ProtectedBytes { get; set; }
        public string? OriginalFileName { get; set; }
        public string? OriginalContentType { get; set; }
        public string? ProtectedOriginal { get; set; }
    }

    private class RemovalRow
    {
        public Guid UploadedBy { get; set; }
        public DateTimeOffset? DeletedAt { get; set; }
    }

    private class EvidenceCaseAccess
    {
        public Guid CustomerId { get; set; }
        public Guid? AssignedTo { get; set; }
        public string QueueType { get; set; } = string.Empty;

        public EvidenceCaseAccess()
        {
        }

        public EvidenceCaseAccess(Guid CustomerId, Guid? AssignedTo, string QueueType)
        {
            this.CustomerId = CustomerId;
            this.AssignedTo = AssignedTo;
            this.QueueType = QueueType;
        }
    }
}
