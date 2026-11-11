using System.Data.Common;
using Dapper;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Repository;
using DisputePortal.Api.Repository.Data;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    private static readonly byte[] RemovalImage = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jhLcAAAAASUVORK5CYII=");

    [Fact]
    public async Task Evidence_removal_retains_bytes_and_records_actor_time_and_one_central_audit()
    {
        var item = await ReviewableRefundCase();
        var repo = new EvidenceRepository(_connections, DatabaseDialect.GetDatabaseDialect(_provider), new TestProtector(), new EvidencePolicyRepository(_connections), DisputePortal.TestFixtures.TestRuntimeConfiguration.Read());
        var id = await repo.AddEvidence(Customer, item.Id, "proof.png", RemovalImage, "Scanned", "ProofOfPayment", default);
        await using var c = await _connections.OpenConnection(default);
        var before = await c.ExecuteScalarAsync<byte[]>(
            """
            SELECT payload."protectedContent" FROM disputes."evidencePayloads" payload
            JOIN disputes.evidence e ON e."payloadId"=payload.id WHERE e."publicId"=@Id
            """,
            new { Id = id });
        await Task.WhenAll(
            repo.RemoveEvidence(Customer, item.Id, id, new() { Reason = "Attached the wrong document." }, default),
            repo.RemoveEvidence(Customer, item.Id, id, new() { Reason = "Concurrent removal request." }, default));
        await repo.RemoveEvidence(Customer, item.Id, id, new() { Reason = "Repeated removal request." }, default);
        var tombstone = Assert.Single(await repo.GetEvidence(Customer, item.Id, default));
        Assert.NotNull(tombstone.DeletedAt);
        Assert.Equal(Customer, tombstone.DeletedById);
        Assert.NotEmpty(tombstone.DeletedBy!);
        Assert.False(tombstone.CanView);
        Assert.False(tombstone.CanRemove);
        Assert.False(tombstone.CanManageAccess);
        Assert.Equal(
            before,
            await c.ExecuteScalarAsync<byte[]>(
                """
            SELECT payload."protectedContent" FROM disputes."evidencePayloads" payload
            JOIN disputes.evidence e ON e."payloadId"=payload.id WHERE e."publicId"=@Id
            """,
                new { Id = id }));
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync(
            """
            UPDATE disputes.evidence SET "fileName"=UPPER("fileName") WHERE "publicId"=@Id
            """,
            new { Id = id }));
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync(
            """
            UPDATE disputes.evidence SET "fileName"="fileName" WHERE "publicId"=@Id
            """,
            new { Id = id }));
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*) FROM audit."auditEvents" WHERE action='RecordSoftDeleted'
                AND "entityType"='evidence' AND "entityPublicId"=@Id
            """,
                new { Id = id }));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => repo.DownloadEvidence(Customer, item.Id, id, default));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => repo.PreviewEvidence(Customer, item.Id, id, default));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => repo.DownloadOriginalEvidence(Customer, item.Id, id, default));
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync(
            "DELETE FROM disputes.evidence WHERE \"publicId\"=@Id",
            new { Id = id }));
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync(
            "UPDATE disputes.evidence SET \"deletedAt\"=NULL,\"deletedById\"=NULL,\"deletionReasonProtected\"=NULL WHERE \"publicId\"=@Id",
            new { Id = id }));
    }

    [Fact]
    public async Task Evidence_removal_checks_ownership_supervisor_rights_and_active_identity()
    {
        var item = await ReviewableRefundCase();
        var repo = new EvidenceRepository(_connections, DatabaseDialect.GetDatabaseDialect(_provider), new TestProtector(), new EvidencePolicyRepository(_connections), DisputePortal.TestFixtures.TestRuntimeConfiguration.Read());
        var id = await repo.AddEvidence(Customer, item.Id, "proof.png", RemovalImage, "Scanned", "ProofOfPayment", default);
        await Assert.ThrowsAsync<PortalNotFoundException>(() => repo.RemoveEvidence(
            OtherCustomer,
            item.Id,
            id,
            new() { Reason = "Attempting a foreign removal." },
            default));
        await Assert.ThrowsAsync<PortalForbiddenException>(() => repo.RemoveEvidence(
            Agent,
            item.Id,
            id,
            new() { Reason = "Only assigned specialist access." },
            default));
        await Assert.ThrowsAsync<PortalValidationException>(() => repo.RemoveEvidence(Customer, item.Id, id, new() { Reason = "" }, default));
        Assert.True(Assert.Single(await repo.GetEvidence(Customer, item.Id, default)).CanView);
    }

    [Fact]
    public async Task Evidence_removal_audit_failure_rolls_back_all_deletion_markers()
    {
        var item = await ReviewableRefundCase();
        var repo = new EvidenceRepository(_connections, DatabaseDialect.GetDatabaseDialect(_provider), new TestProtector(), new EvidencePolicyRepository(_connections), DisputePortal.TestFixtures.TestRuntimeConfiguration.Read());
        var id = await repo.AddEvidence(Customer, item.Id, "proof.png", RemovalImage, "Scanned", "ProofOfPayment", default);
        await using var c = await _connections.OpenConnection(default);
        // A conflicting immutable receipt forces the real native audit insert to fail.
        await c.ExecuteAsync(
            """
            INSERT INTO audit."auditEvents" ("publicId","actorUserId",action,"entityType","entityPublicId","correlationId",outcome)
            VALUES (@Audit,(SELECT id FROM "identity".users WHERE "publicId"=@Actor),'RecordSoftDeleted','evidence',@Id,'failure-test','Success')
            """,
            new
            {
                Audit = Guid.NewGuid(),
                Actor = Customer,
                Id = id
            });
        await Assert.ThrowsAnyAsync<DbException>(() => repo.RemoveEvidence(
            Customer,
            item.Id,
            id,
            new() { Reason = "This must roll back with audit." },
            default));
        Assert.Null(Assert.Single(await repo.GetEvidence(Customer, item.Id, default)).DeletedAt);
        Assert.Equal(RemovalImage, (await repo.DownloadEvidence(Customer, item.Id, id, default)).Content);
    }

    [Fact]
    public async Task Important_case_and_bank_records_reject_physical_deletion_and_history_tampering()
    {
        var item = await ReviewableRefundCase();
        await using var c = await _connections.OpenConnection(default);
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync(
            "DELETE FROM disputes.disputes WHERE \"publicId\"=@Id",
            new { Id = item.Id }));
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync(
            "DELETE FROM transactions.transactions WHERE \"publicId\"=@Id",
            new { Id = Transaction }));
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync("UPDATE disputes.\"statusHistory\" SET comment='Altered history'"));
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync("DELETE FROM audit.\"auditEvents\""));
    }

    [Fact]
    public async Task Native_soft_removal_requires_valid_actor_and_preserves_account_audit_history()
    {
        await using var c = await _connections.OpenConnection(default);
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync(
            """
            UPDATE accounts.accounts SET "accountStatus"='Closed',"deletedAt"=@Now WHERE "publicId"=@Id
            """,
            new { Now = DateTimeOffset.UtcNow, Id = Account }));
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync(
            """
            UPDATE accounts.accounts SET "accountStatus"='Closed',"deletedAt"=@Now,"deletedById"=-1 WHERE "publicId"=@Id
            """,
            new { Now = DateTimeOffset.UtcNow, Id = Account }));
        await c.ExecuteAsync(
            """
            UPDATE accounts.accounts SET "accountStatus"='Closed',"deletedAt"=@Now,
                "deletedById"=(SELECT id FROM "identity".users WHERE "publicId"=@Actor)
            WHERE "publicId"=@Id
            """,
            new
            {
                Now = DateTimeOffset.UtcNow,
                Actor = Customer,
                Id = Account
            });
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*) FROM audit."auditEvents" a JOIN "identity".users u ON u.id=a."actorUserId"
            WHERE a.action='RecordSoftDeleted' AND a."entityType"='account'
                AND a."entityPublicId"=@Id AND u."publicId"=@Actor
            """,
                new { Id = Account, Actor = Customer }));
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync(
            """
            UPDATE accounts.accounts SET "deletedAt"=NULL,"deletedById"=NULL WHERE "publicId"=@Id
            """,
            new { Id = Account }));
    }

    [Fact]
    public async Task Supervisor_can_remove_foreign_upload_and_disabled_uploaders_cannot_remove()
    {
        var item = await ReviewableRefundCase();
        var repo = new EvidenceRepository(_connections, DatabaseDialect.GetDatabaseDialect(_provider), new TestProtector(), new EvidencePolicyRepository(_connections), DisputePortal.TestFixtures.TestRuntimeConfiguration.Read());
        var id = await repo.AddEvidence(Customer, item.Id, "proof.png", RemovalImage, "Scanned", "ProofOfPayment", default);
        await GrantRefundSupervisor(OtherCustomer, "operations_manager", default);
        await using var c = await _connections.OpenConnection(default);
        await c.ExecuteAsync("UPDATE \"identity\".users SET \"accountStatus\"='Disabled' WHERE \"publicId\"=@Id", new { Id = Customer });
        await Assert.ThrowsAsync<PortalForbiddenException>(() => repo.RemoveEvidence(
            Customer,
            item.Id,
            id,
            new() { Reason = "Inactive uploader request." },
            default));
        await repo.RemoveEvidence(OtherCustomer, item.Id, id, new() { Reason = "Supervisor retained the incorrect upload." }, default);
        Assert.Equal(OtherCustomer, Assert.Single(await repo.GetEvidence(OtherCustomer, item.Id, default)).DeletedById);
    }
}
