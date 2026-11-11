using DisputePortal.Api.Models.Domain;
using Dapper;
using System.Data.Common;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Repository.Data;
using DisputePortal.Api.Service.Disputes;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Policy_defaults_reset_is_super_admin_only_versioned_atomic_and_audited()
    {
        var ct = CancellationToken.None;
        await using var c = await _connections.OpenConnection(ct);
        await c.ExecuteAsync(
            """
            INSERT INTO "identity"."userRoles"("userId",
                "roleId")
            SELECT(
                SELECT "resolved1"."id"
                FROM "identity"."users" AS "resolved1"
                WHERE "resolved1"."publicId" = @Id),
            "roles"."id"
            FROM "identity"."roles" AS "roles"
            WHERE "roles"."code" = 'operations_manager'
            """,
            new { Id = Agent });
        var baseline = await _repository.GetPolicyDefaults(ct);
        Assert.True(baseline.Submission.HighValueThreshold > 0);
        Assert.True(baseline.Automatic.MaximumAmount > 0);
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync("""
            UPDATE configuration."riskPolicies"
            SET "highValueThreshold" = "highValueThreshold" + 1
            WHERE "version" = 0;
            """));
        var finance = Finance();
        var initialRisk = await _repository.GetRiskPolicy(ct);
        var initialAutomaticVersion = await c.ExecuteScalarAsync<int>("SELECT \"lastVersion\" FROM configuration.\"refundPolicies\" WHERE code='Default'");
        var request = new ResetDisputePoliciesRequest(
            initialRisk.Version,
            initialAutomaticVersion,
            "Restore the organisation defaults for new submissions.");
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.ResetPolicies(Agent, request, ct));
        await c.ExecuteAsync(
            """
            INSERT INTO "identity"."userRoles"("userId",
                "roleId")
            SELECT(
                SELECT "resolved1"."id"
                FROM "identity"."users" AS "resolved1"
                WHERE "resolved1"."publicId" = @Id),
            "roles"."id"
            FROM "identity"."roles" AS "roles"
            WHERE "roles"."code" = 'super_admin'
            """,
            new { Id = Agent });
        await _repository.UpdateRiskPolicy(
            Agent,
            new(initialRisk.Version, 2_000_000m, 365, 365, 25_000m, "Increase the configured routing thresholds and review age."),
            ct);
        await finance.UpdatePolicy(
            Agent,
            new(initialAutomaticVersion, false, 2_500m, 720, 10_000m, "Increase the configured automatic refund limits."),
            ct);
        var beforeRisk = await _repository.GetRiskPolicy(ct);
        var beforeAutomatic = await finance.GetPolicy(Agent, ct);
        request = new ResetDisputePoliciesRequest(request)
        {
            ExpectedRiskVersion = beforeRisk.Version,
            ExpectedAutomaticVersion = beforeAutomatic.Version
        };
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.ResetPolicies(
            Agent,
            new ResetDisputePoliciesRequest(request) { ExpectedRiskVersion = beforeRisk.Version - 1 },
            ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.ResetPolicies(
            Agent,
            new ResetDisputePoliciesRequest(request) { ExpectedAutomaticVersion = beforeAutomatic.Version - 1 },
            ct));
        // Reject the second update, after the risk-policy update has already run.
        await c.ExecuteAsync("ALTER TABLE configuration.\"refundPolicies\" ADD CONSTRAINT ck_test_block_policy_defaults CHECK(version<=" + beforeAutomatic.Version + " OR enabled=" + DatabaseDialect.GetDatabaseDialect(_provider).FalseLiteral + ")");
        try
        {
            await Assert.ThrowsAnyAsync<DbException>(() => _repository.ResetPolicies(Agent, request, ct));
            Assert.Equivalent(beforeRisk, await _repository.GetRiskPolicy(ct), strict: true);
            var unchangedAutomatic = await finance.GetPolicy(Agent, ct);
            Assert.Equal(
                (beforeAutomatic.Version, beforeAutomatic.Enabled, beforeAutomatic.MaximumAmount, beforeAutomatic.MaximumAgeHours, beforeAutomatic.DailyLimit),
                (unchangedAutomatic.Version, unchangedAutomatic.Enabled, unchangedAutomatic.MaximumAmount, unchangedAutomatic.MaximumAgeHours, unchangedAutomatic.DailyLimit));
            Assert.Equal(
                0,
                await c.ExecuteScalarAsync<int>("""
                SELECT COUNT(*)
                FROM "audit"."auditEvents" AS "admin_audit_logs"
                WHERE "admin_audit_logs"."action" = 'DisputePolicyDefaultsRestored'
                """));
        }
        finally
        {
            await c.ExecuteAsync("ALTER TABLE configuration.\"refundPolicies\" DROP CONSTRAINT ck_test_block_policy_defaults");
        }

        var result = await _repository.ResetPolicies(Agent, request, ct);
        Assert.Equivalent(
            new global::DisputePortal.Api.Models.Domain.DisputeRiskPolicy(baseline.Submission) { Version = beforeRisk.Version + 1 },
            result.Submission,
            strict: true);
        Assert.True(result.Automatic.Enabled);
        Assert.Equal(baseline.Automatic.MaximumAmount, result.Automatic.MaximumAmount);
        Assert.Equal(48, result.Automatic.MaximumAgeHours);
        Assert.Equal(baseline.Automatic.DailyLimit, result.Automatic.DailyLimit);
        Assert.Equal(beforeAutomatic.Version + 1, result.Automatic.Version);
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "audit"."auditEvents" AS "admin_audit_logs"
            WHERE "admin_audit_logs"."action" = 'DisputePolicyDefaultsRestored'
            AND(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "admin_audit_logs"."actorUserId") = @Actor
            """,
                new { Actor = Agent }));
        var replay = await _repository.ResetPolicies(Agent, request, ct);
        Assert.Equal(result.Automatic.Version, replay.Automatic.Version);
        Assert.Equal(result.Submission.Version, replay.Submission.Version);
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM audit.\"auditEvents\" WHERE action='DisputePolicyDefaultsRestored'"));
    }

    [Fact]
    public async Task Archived_transactions_cannot_start_a_new_case_or_automatic_refund_decision()
    {
        var ct = CancellationToken.None;
        await using var c = await _connections.OpenConnection(ct);
        await c.ExecuteAsync(
            """
            UPDATE "transactions"."transactions"
            SET "archivedAt" = @Now
            WHERE "transactions"."publicId" = @Id
            """,
            new { Id = Transaction, Now = DateTimeOffset.UtcNow });
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Duplicate",
                Reason = "The payment was charged twice",
                Description = "Investigate this archived payment."
            },
            "archived-intake",
            ct));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.AssessDispute(Customer, Transaction, ct));
        // A pending claim on a second payment becomes history when its source is archived.
        var created = await _repository.CreateDispute(
            OtherCustomer,
            new CreateDisputeCommand
            {
                TransactionId = OtherTransaction,
                Category = "Duplicate",
                Reason = "The payment was charged twice",
                Description = "Investigate this duplicate payment."
            },
            "archived-auto",
            ct);
        await c.ExecuteAsync(
            """
            UPDATE "transactions"."transactions"
            SET "archivedAt" = @Now
            WHERE "transactions"."publicId" = @Id
            """,
            new { Id = OtherTransaction, Now = DateTimeOffset.UtcNow });
        Assert.False(await Finance().ProcessAutomatic(ct));
        Assert.Null((await Finance().GetCaseFinancials(OtherCustomer, created.Id, ct)).Decision);
        Assert.NotNull(await _repository.GetCustomerDispute(OtherCustomer, created.Id, ct));
    }
}
