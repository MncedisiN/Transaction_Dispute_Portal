using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Service.Disputes;
using Dapper;
using System.Data.Common;
using DisputePortal.Api.Repository;
using DisputePortal.Api.Repository.Data;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Exceptions;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    private PolicyVersionRepository VersionStore() => new(_connections, DatabaseDialect.GetDatabaseDialect(_provider));

    private async Task PromotePolicyAdministrator()
    {
        await using var c = await _connections.OpenConnection(default);
        await c.ExecuteAsync(
            """
            INSERT INTO "identity"."userRoles"("userId","roleId")
            SELECT u.id,r.id FROM "identity".users u CROSS JOIN "identity".roles r
            WHERE u."publicId"=@Actor AND r.code='super_admin'
            AND NOT EXISTS(SELECT 1 FROM "identity"."userRoles" assigned WHERE assigned."userId"=u.id AND assigned."roleId"=r.id)
            """,
            new { Actor = Agent });
    }

    [Fact]
    public async Task Policy_publication_keeps_original_snapshot_and_replays_one_request()
    {
        await PromotePolicyAdministrator();
        var store = VersionStore();
        var before = await store.AutomaticHistory(Agent, 1, 20, null, default);
        var original = before.Effective.Selected!;
        Assert.Equal(10, original.DuplicateWindowMinutes);
        var request = new UpdateAutomaticRefundPolicyRequest(
            original.Version,
            true,
            2000,
            100,
            10000,
            "Publish the approved complete refund settings.")
        {
            ConcurrencyToken = before.ConcurrencyToken,
            DuplicateWindowMinutes = 7
        };
        await store.PublishAutomatic(Agent, request, default);
        await store.PublishAutomatic(Agent, request, default);
        var after = await store.AutomaticHistory(Agent, 1, 20, null, default);
        Assert.Equal(before.Total + 1, after.Total);
        Assert.Equal(original.Version + 1, after.Effective.Selected!.Version);
        var old = after.Items.Single(x => x.Version == original.Version);
        Assert.Equal(7, after.Effective.Selected!.DuplicateWindowMinutes);
        Assert.Equal(10, old.DuplicateWindowMinutes);
        Assert.Equal(original.MaximumAmount, old.MaximumAmount);
        Assert.Equal(original.VersionPublicId, old.VersionPublicId);
        request.MaximumAmount = 1999;
        await Assert.ThrowsAsync<PortalConflictException>(() => store.PublishAutomatic(Agent, request, default));
        await using var c = await _connections.OpenConnection(default);
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync(
            """UPDATE configuration."refundPolicies" SET "maximumAmount"=123 WHERE "publicId"=@Id""",
            new { Id = old.VersionPublicId }));
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync(
            """DELETE FROM configuration."refundPolicies" WHERE "publicId"=@Id""",
            new { Id = old.VersionPublicId }));
    }

    [Fact]
    public async Task Policy_schedule_preview_and_deletion_do_not_fall_back_or_rewrite_attribution()
    {
        await PromotePolicyAdministrator();
        var store = VersionStore();
        var before = await store.AutomaticHistory(Agent, 1, 20, null, default);
        var request = new UpdateAutomaticRefundPolicyRequest(
            before.Effective.Selected!.Version,
            false,
            250,
            48,
            500,
            "Disable automated refunds for an incident.")
        {
            ConcurrencyToken = before.ConcurrencyToken
        };
        var preview = await store.PreviewAutomatic(Agent, request, default);
        Assert.False(preview.After.Selected!.Enabled);
        Assert.Equal(before.Total, (await store.AutomaticHistory(Agent, 1, 20, null, default)).Total);
        await store.PublishAutomatic(Agent, request, default);
        var current = await store.AutomaticHistory(Agent, 1, 20, null, default);
        Assert.False(current.Effective.Selected!.Enabled);
        var remove = new DeletePolicyVersionRequest
        {
            ConcurrencyToken = current.ConcurrencyToken,
            Reason = "Remove the shutdown publication after review."
        };
        var version = current.Effective.Selected.VersionPublicId;
        Assert.Null((await store.SoftDeletePolicyVersion(Agent, true, version, remove, true, default)).Selected);
        await store.SoftDeletePolicyVersion(Agent, true, version, remove, false, default);
        var removed = (await store.AutomaticHistory(Agent, 1, 20, null, default)).Items.Single(x => x.VersionPublicId == version);
        Assert.True(removed.IsDeleted);
        Assert.Equal(Agent, removed.DeletedBy);
        Assert.NotNull(removed.DeletedAtUtc);
        await store.SoftDeletePolicyVersion(
            Agent,
            true,
            version,
            new DeletePolicyVersionRequest
            {
                ConcurrencyToken = 0,
                Reason = "A repeated removal must retain its first actor."
            },
            false,
            default);
        var repeated = await store.AutomaticHistory(Agent, 1, 20, null, default);
        Assert.Null(repeated.Effective.Selected);
        Assert.Equal(removed.DeletedAtUtc, repeated.Items.Single(x => x.VersionPublicId == version).DeletedAtUtc);
        var restore = new UpdateAutomaticRefundPolicyRequest
        {
            ConcurrencyToken = repeated.ConcurrencyToken,
            RequestId = Guid.NewGuid(),
            SourceVersionPublicId = version,
            Operation = "Restore",
            Reason = "Restore the retained snapshot as a fresh publication."
        };
        await store.PublishAutomatic(Agent, restore, default);
        var restored = await store.AutomaticHistory(Agent, 1, 20, null, default);
        Assert.Equal(removed.Version + 1, restored.Effective.Selected!.Version);
        Assert.False(restored.Effective.Selected.Enabled);
    }

    [Fact]
    public async Task Concurrent_policy_saves_cannot_allocate_the_same_revision_or_lose_a_save()
    {
        await PromotePolicyAdministrator();
        var store = VersionStore();
        var before = await store.AutomaticHistory(Agent, 1, 20, null, default);
        async Task<bool> Save(decimal maximum)
        {
            var r = new UpdateAutomaticRefundPolicyRequest(
                before.Effective.Selected!.Version,
                true,
                maximum,
                48,
                1000,
                "Review and publish concurrent administrative changes.")
            {
                ConcurrencyToken = before.ConcurrencyToken
            };
            try
            {
                await VersionStore().PublishAutomatic(Agent, r, default);
                return true;
            }
            catch (PortalConflictException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(Save(300), Save(400));
        Assert.Single(results, x => x);
        Assert.Equal(before.Total + 1, (await store.AutomaticHistory(Agent, 1, 20, null, default)).Total);
    }

    [Fact]
    public async Task Future_publication_is_scheduled_and_cannot_be_overridden_by_an_earlier_start()
    {
        await PromotePolicyAdministrator();
        var store = VersionStore();
        var history = await store.AutomaticHistory(Agent, 1, 20, null, default);
        var r = new UpdateAutomaticRefundPolicyRequest(
            history.Effective.Selected!.Version,
            true,
            300,
            48,
            1000,
            "Schedule the next reviewed refund publication.")
        {
            ConcurrencyToken = history.ConcurrencyToken,
            EffectiveFromUtc = DateTimeOffset.UtcNow.AddDays(1),
            EffectiveToUtc = DateTimeOffset.UtcNow.AddDays(2)
        };
        await store.PublishAutomatic(Agent, r, default);
        var next = await store.AutomaticHistory(Agent, 1, 20, null, default);
        Assert.Equal(history.Effective.Selected.Version, next.Effective.Selected!.Version);
        Assert.Equal("Scheduled", next.Items.First().State);
        r.RequestId = Guid.NewGuid();
        r.ConcurrencyToken = next.ConcurrencyToken;
        r.EffectiveFromUtc = null;
        await Assert.ThrowsAsync<PortalConflictException>(() => store.PublishAutomatic(Agent, r, default));
    }

    [Fact]
    public async Task A_disabled_routing_publication_blocks_intake_without_using_an_old_enabled_version()
    {
        await PromotePolicyAdministrator();
        var store = VersionStore();
        var before = await store.RoutingHistory(Agent, 1, 20, null, default);
        var r = before.Effective.Selected!;
        var request = new RiskPolicyUpdateRequest(
            r.Version,
            r.HighValueThreshold,
            r.ExtraCheckAfterDays,
            r.DebitOrderWindowDays,
            r.DebitOrderAppLimit,
            "Temporarily stop new dispute submissions for review.")
        {
            Enabled = false,
            ConcurrencyToken = before.ConcurrencyToken
        };
        await store.PublishRouting(Agent, request, default);
        Assert.False((await store.RoutingHistory(Agent, 1, 20, null, default)).Effective.Selected!.Enabled);
        Assert.False((await _repository.AssessDispute(Customer, Transaction, default)).Allowed);
    }

    [Fact]
    public async Task Routing_publication_invalidates_prior_verification_without_bypassing_otp_cooldown()
    {
        var ct = CancellationToken.None;
        await using var c = await _connections.OpenConnection(ct);
        await c.ExecuteAsync("""UPDATE transactions.transactions SET amount=6000 WHERE "publicId"=@Transaction""", new { Transaction });
        var session = await _repository.CreateSession(Customer, DateTimeOffset.UtcNow.AddMinutes(30), ct);
        var challenge = await _repository.ReserveDisputeVerification(
            Customer,
            session,
            Transaction,
            "email",
            "customer@test.local",
            "123456",
            ct);
        await _repository.ReadyDisputeVerification(Customer, challenge.ChallengeId, ct);
        await _repository.CompleteDisputeVerification(Customer, session, Transaction, challenge.ChallengeId, "123456", ct);
        await PromotePolicyAdministrator();
        var history = await VersionStore().RoutingHistory(Agent, 1, 20, null, ct);
        var old = history.Effective.Selected!;
        await VersionStore().PublishRouting(
            Agent,
            new RiskPolicyUpdateRequest(
                old.Version,
                old.HighValueThreshold,
                old.ExtraCheckAfterDays,
                old.DebitOrderWindowDays,
                old.DebitOrderAppLimit,
                "Publish a new routing security review revision.")
            { ConcurrencyToken = history.ConcurrencyToken },
            ct);
        var command = new CreateDisputeCommand
        {
            TransactionId = Transaction,
            Category = "Unrecognized",
            Reason = "I did not make this transaction",
            Description = "Review this transaction security challenge.",
            VerificationId = challenge.ChallengeId,
            SessionId = session
        };
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.CreateDispute(Customer, command, "changed-policy-check", ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.ReserveDisputeVerification(
            Customer,
            session,
            Transaction,
            "email",
            "customer@test.local",
            "123456",
            ct));
        var recorded = await c.QuerySingleAsync<Guid>(new CommandDefinition(
            """
            SELECT p."publicId" FROM "identity"."disputeVerifications" v
            JOIN configuration."riskPolicies" p ON p.id=v."riskPolicyVersionId"
            WHERE v."publicId"=@Challenge
            """,
            new { Challenge = challenge.ChallengeId },
            cancellationToken: ct));
        Assert.Equal(old.VersionPublicId, recorded);
    }

    [Fact]
    public async Task Native_priority_resolution_and_equal_priority_overlap_are_consistent()
    {
        await PromotePolicyAdministrator();
        await using var c = await _connections.OpenConnection(default);
        var other = Guid.NewGuid();
        var snapshot = Guid.NewGuid();
        var groupId = await c.ExecuteScalarAsync<long>("SELECT COALESCE(MAX(id), 0) + 1 FROM configuration.\"refundPolicies\"");
        if (_provider == DatabaseProvider.SqlServer)
        {
            await c.ExecuteAsync("SET IDENTITY_INSERT configuration.\"refundPolicies\" ON");
        }

        await c.ExecuteAsync("""
            INSERT INTO configuration."refundPolicies" (
                "id", "policyId", "publicId", "version", "priority", "effectiveFromUtc",
                "changeReason", "provenance", "enabled", "maximumAmount", "maximumAgeHours",
                "dailyLimit", "duplicateWindowMinutes", "configurationKey", "initialVersion",
                "code", "lastVersion", "concurrencyToken", "scope"
            )
            VALUES (
                @GroupId, @GroupId, @Other, 0, 0, @At,
                'Initial incident policy definition.', 'InitialDefaults', @Enabled, 100, 48,
                500, 10, @GroupId, 1, 'Incident', 1, 1, 'Global'
            )
            """, new { GroupId = groupId, Other = other, At = DateTimeOffset.UtcNow.AddMinutes(-1), Enabled = false });
        if (_provider == DatabaseProvider.SqlServer)
        {
            await c.ExecuteAsync("SET IDENTITY_INSERT configuration.\"refundPolicies\" OFF");
        }
        else
        {
            await c.ExecuteScalarAsync<long>("SELECT setval(pg_get_serial_sequence('configuration.\"refundPolicies\"', 'id'), (SELECT MAX(id) FROM configuration.\"refundPolicies\"))");
        }

        await c.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO configuration."refundPolicies"("policyId","publicId",version,priority,"effectiveFromUtc","publishedAtUtc","changeReason",provenance,enabled,"maximumAmount","maximumAgeHours","dailyLimit","duplicateWindowMinutes")
            SELECT id,@Snapshot,1,100,@At,@At,'Reviewed high priority incident policy.','Published',@Enabled,100,48,500,10
            FROM configuration."refundPolicies" WHERE "publicId"=@Other;
            """,
            new
            {
                Other = other,
                Snapshot = snapshot,
                At = DateTimeOffset.UtcNow.AddMinutes(-1),
                Enabled = false
            }));
        var store = VersionStore();
        var history = await store.AutomaticHistory(Agent, 1, 20, null, default);
        Assert.Equal(snapshot, history.Effective.Selected!.VersionPublicId);
        Assert.False(history.Effective.Selected.Enabled);
        var request = new UpdateAutomaticRefundPolicyRequest
        {
            RequestId = Guid.NewGuid(),
            ConcurrencyToken = history.ConcurrencyToken,
            Enabled = true,
            MaximumAmount = 200,
            MaximumAgeHours = 48,
            DailyLimit = 500,
            Priority = 100,
            Reason = "Review the overlapping priority schedule before publication."
        };
        await Assert.ThrowsAsync<PortalConflictException>(() => store.PublishAutomatic(Agent, request, default));
        request.Priority = 101;
        request.RequestId = Guid.NewGuid();
        await store.PublishAutomatic(Agent, request, default);
        Assert.Equal(101, (await store.AutomaticHistory(Agent, 1, 20, null, default)).Effective.Selected!.Priority);
    }
    [Fact]
    public async Task Financial_policy_references_require_a_published_revision_and_a_valid_family()
    {
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "Not recognised",
                Description = "Verify the immutable published policy reference."
            },
            "policy-reference-guard",
            default);
        await using var connection = await _connections.OpenConnection(default);
        var family = await connection.ExecuteScalarAsync<long>(
            "SELECT id FROM configuration.\"riskPolicies\" WHERE version = 0 AND code = 'Default'");
        var published = await connection.ExecuteScalarAsync<long>(
            "SELECT MIN(id) FROM configuration.\"riskPolicies\" WHERE version > 0");
        await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(() => connection.ExecuteAsync(
            """
            UPDATE disputes."disputes"
            SET "riskPolicyVersionId" = @Family
            WHERE "publicId" = @Case
            """, new { Family = family, Case = created.Id }));
        Assert.Equal(published, await connection.ExecuteScalarAsync<long>(
            """SELECT "riskPolicyVersionId" FROM disputes."disputes" WHERE "publicId" = @Case""",
            new { Case = created.Id }));

        const string invalidPublication = """
            INSERT INTO configuration."riskPolicies" (
                "policyId",
                "supersedesVersionId",
                "publicId",
                "version",
                "effectiveFromUtc",
                "publishedAtUtc",
                "changeReason",
                "provenance",
                "enabled",
                "highValueThreshold",
                "extraCheckAfterDays",
                "debitOrderWindowDays",
                "debitOrderAppLimit"
            )
            SELECT
                @Parent,
                @Predecessor,
                @Id,
                "lastVersion" + 1,
                @Now,
                @Now,
                'Verify publication reference integrity.',
                'Published',
                "enabled",
                "highValueThreshold",
                "extraCheckAfterDays",
                "debitOrderWindowDays",
                "debitOrderAppLimit"
            FROM configuration."riskPolicies"
            WHERE "id" = @Family
            """;
        var wrongFamily = await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(() => connection.ExecuteAsync(
            invalidPublication,
            new { Parent = published, Predecessor = (long?)null, Id = Guid.NewGuid(), Now = DateTimeOffset.UtcNow, Family = family }));
        Assert.Contains("family", wrongFamily.Message, StringComparison.OrdinalIgnoreCase);
        var wrongPredecessor = await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(() => connection.ExecuteAsync(
            invalidPublication,
            new { Parent = family, Predecessor = family, Id = Guid.NewGuid(), Now = DateTimeOffset.UtcNow, Family = family }));
        Assert.Contains("predecessor", wrongPredecessor.Message, StringComparison.OrdinalIgnoreCase);
    }

}
