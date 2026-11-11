using Dapper;
using System.Data.Common;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Service.Disputes;
using DisputePortal.Api.Models.Requests;
using Xunit;
using DisputePortal.Api.Models.Domain;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Actor_snapshots_preserve_action_time_names_roles_and_persisted_ids_without_exposing_logins_to_customers()
    {
        var ct = CancellationToken.None;
        await using var c = await _connections.OpenConnection(ct);
        await c.ExecuteAsync(
            """
            UPDATE "identity"."customerProfiles"
            SET "firstName" = 'Alex',
            "lastName" = 'Morgan',
            "displayName" = 'Alex Morgan'
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "customerProfiles"."userId") = @Customer;
            UPDATE "identity"."customerProfiles"
            SET "firstName" = 'Kabelo',
            "middleName" = 'J',
            "lastName" = 'Dube',
            "displayName" = 'internal-staff-account'
            WHERE(
                SELECT "related2"."publicId"
                FROM "identity"."users" AS "related2"
                WHERE "related2"."id" = "customerProfiles"."userId") = @Agent
            """,
            new { Customer, Agent });
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "Not recognised",
                Description = "Please investigate this unauthorised payment."
            },
            "actor-audit",
            ct);
        var detail = (await _repository.GetCustomerDispute(Customer, created.Id, ct))!;
        Assert.Equal("Alex Morgan", detail.History[0].ActorName);
        await _repository.TransitionDispute(
            Agent,
            created.Id,
            new TransitionCommand(detail.Dispute.Version, "UnderReview", "Investigation started."),
            "actor-trace",
            ct);
        await _repository.SendMessage(Agent, new SupportSendRequest(null, created.Id, "Case update", "Your investigation has started."), ct);
        await c.ExecuteAsync(
            """
            UPDATE "identity"."customerProfiles"
            SET "firstName" = 'Changed',
            "lastName" = 'Person',
            "displayName" = 'Changed Person'
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "customerProfiles"."userId") = @Agent;
            DELETE
            FROM "identity"."userRoles"
            WHERE(
                SELECT "related2"."publicId"
                FROM "identity"."users" AS "related2"
                WHERE "related2"."id" = "userRoles"."userId") = @Agent;
            INSERT INTO "identity"."userRoles"("userId",
                "roleId")
            SELECT(
                SELECT "resolved3"."id"
                FROM "identity"."users" AS "resolved3"
                WHERE "resolved3"."publicId" = @Agent),
            "roles"."id"
            FROM "identity"."roles" AS "roles"
            WHERE "roles"."code" = 'payments_specialist'
            """,
            new { Agent });
        detail = (await _repository.GetCustomerDispute(Customer, created.Id, ct))!;
        var identity = detail.History.Single(h => h.NewStatus == "UnderReview").ActorIdentity!;
        Assert.Equal("Kabelo J Dube", identity.FullName);
        Assert.Empty(identity.Roles);
        Assert.Null(identity.Provenance);
        Assert.Null(identity.UserId);
        Assert.Null(identity.LoginIdentifier);
        Assert.Equal("Kabelo J Dube", identity.AccountName);
        using (var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(
            detail,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))))
        {
            var publicActor = json.RootElement.GetProperty("history").EnumerateArray().Single(row => row.GetProperty("newStatus").GetString() == "UnderReview").GetProperty("actorIdentity");
            Assert.False(publicActor.TryGetProperty("provenance", out _));
            Assert.Equal(System.Text.Json.JsonValueKind.Null, publicActor.GetProperty("userId").ValueKind);
            Assert.Equal(System.Text.Json.JsonValueKind.Null, publicActor.GetProperty("loginIdentifier").ValueKind);
            Assert.Equal(0, publicActor.GetProperty("roles").GetArrayLength());
            Assert.Equal("Kabelo J Dube", publicActor.GetProperty("accountName").GetString());
        }

        var activity = await Finance().GetCaseActivity(Customer, created.Id, ct);
        var reviewed = activity.Single(a => a.Action == "UnderReview");
        Assert.Equal("Kabelo J Dube", reviewed.Actor);
        Assert.Null(reviewed.ActorIdentity!.LoginIdentifier);
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "disputes"."statusHistory" AS "dispute_status_history"
            WHERE "dispute_status_history"."publicId" = @Id
            AND(
                SELECT "related1"."publicId"
                FROM "disputes"."disputes" AS "related1"
                WHERE "related1"."id" = "dispute_status_history"."disputeId") = @Dispute
            """,
                new { Id = Guid.Parse(reviewed.Id), Dispute = created.Id }));
        var thread = Assert.Single(await _repository.GetThreads(Customer, ct, created.Id));
        var message = Assert.Single(await _repository.GetMessages(Customer, thread.Id, ct));
        Assert.Equal("Kabelo J Dube", message.AuthorName);
        Assert.Empty(message.ActorIdentity!.Roles);
        Assert.Null(message.ActorIdentity.LoginIdentifier);
        var audit = await _repository.GetAuditLogs(1, 100, "DisputeStatusChanged", ct);
        var action = Assert.Single(audit.Items);
        Assert.Equal(Agent, action.ActorIdentity!.UserId);
        Assert.Equal("agent@test.local", action.ActorIdentity.LoginIdentifier);
        Assert.Equal("Kabelo J Dube", action.ActorName);
        Assert.Equal("actor-trace", action.CorrelationId);
        Assert.Equal("Recorded", action.ActorIdentity.Provenance);
        Assert.Equal("internal-staff-account", action.ActorIdentity.AccountName);
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "audit"."auditEvents" AS "admin_audit_logs"
            WHERE "admin_audit_logs"."publicId" = @Id
            AND "admin_audit_logs"."entityPublicId" = @Case
            """,
                new { action.Id, Case = created.Id }));
        var transaction = (await _repository.GetTransaction(Customer, Transaction, ct))!;
        var stored = await c.QuerySingleAsync<(Guid Id, string Reference)>(
            """
            SELECT "transactions"."publicId" AS "id",
            "transactions"."transactionReference" AS "transaction_reference"
            FROM "transactions"."transactions" AS "transactions"
            WHERE "transactions"."publicId" = @Id
            """,
            new { Id = Transaction });
        Assert.Equal(stored.Id, transaction.Id);
        Assert.Null(transaction.Reference);
        Assert.Equal(transaction.Id, detail.Dispute.TransactionId);
        Assert.Null(detail.TransactionReference);
    }

    [Fact]
    public async Task Actor_snapshots_handle_multirow_system_and_anonymous_events_and_reject_rewrites()
    {
        var ct = CancellationToken.None;
        await using var c = await _connections.OpenConnection(ct);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var security = Guid.NewGuid();
        await c.ExecuteAsync(
            """
            INSERT INTO "audit"."auditEvents"("publicId",
                "actorUserId",
                "action",
                "entityType",
                "entityPublicId",
                "correlationId")
            VALUES(@First,
                (
                    SELECT "resolved1"."id"
                    FROM "identity"."users" AS "resolved1"
                    WHERE "resolved1"."publicId" = @Customer),
                'SnapshotCheck',
                'user',
                @Customer,
                'snapshot-one'),
            (@Second,
                NULL,
                'SnapshotCheck',
                'user',
                NULL,
                'snapshot-two');
            INSERT INTO "identity"."securityEvents"("publicId",
                "userId",
                "eventType",
                "outcome",
                "ipAddress",
                "userAgent",
                "deviceType",
                "browser",
                "location",
                "occurredAt")
            VALUES(@Security,
                NULL,
                'PasswordCheck',
                'Failed',
                '127.0.0.1',
                'Test',
                'Desktop',
                'Test',
                'Local development',
                @Now)
            """,
            new
            {
                First = first,
                Second = second,
                Security = security,
                Customer,
                Now = DateTimeOffset.UtcNow
            });
        Assert.Equal(
            2,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "audit"."actorSnapshots" AS "action_actor_snapshots"
            WHERE "action_actor_snapshots"."sourceKind" = 'Audit'
            AND(
                SELECT "related1"."publicId"
                FROM "audit"."auditEvents" AS "related1"
                WHERE "related1"."id" = "action_actor_snapshots"."auditEventId") IN(@First,
                @Second)
            """,
                new { First = first, Second = second }));
        var audit = await _repository.GetAuditLogs(1, 100, "SnapshotCheck", ct);
        Assert.Equal("System", audit.Items.Single(a => a.Id == second).ActorIdentity!.Kind);
        var missing = audit.Items.Single(a => a.Id == first).ActorIdentity!;
        Assert.Null(missing.FullName);
        Assert.Equal("Test Customer", missing.AccountName);
        Assert.Equal("User", missing.Kind);
        var anonymous = Assert.Single((await _repository.GetAuditLogs(1, 100, "PasswordCheck", ct)).Items);
        Assert.Equal("Anonymous", anonymous.ActorIdentity!.Kind);
        Assert.Equal("Unauthenticated request", anonymous.ActorName);
        Assert.Null(anonymous.ActorIdentity.UserId);
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync(
            """
            UPDATE "audit"."actorSnapshots"
            SET "accountName" = 'Someone else'
            WHERE "actorSnapshots"."sourceKind" = 'Audit'
            AND(
                SELECT "related1"."publicId"
                FROM "audit"."auditEvents" AS "related1"
                WHERE "related1"."id" = "actorSnapshots"."auditEventId") = @Id
            """,
            new { Id = first }));
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync(
            """
            DELETE
            FROM "audit"."actorSnapshots"
            WHERE "actorSnapshots"."sourceKind" = 'Audit'
            AND(
                SELECT "related1"."publicId"
                FROM "audit"."auditEvents" AS "related1"
                WHERE "related1"."id" = "actorSnapshots"."auditEventId") = @Id
            """,
            new { Id = second }));
        Assert.Equal(
            2,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "audit"."actorSnapshots" AS "action_actor_snapshots"
            WHERE "action_actor_snapshots"."sourceKind" = 'Audit'
            AND(
                SELECT "related1"."publicId"
                FROM "audit"."auditEvents" AS "related1"
                WHERE "related1"."id" = "action_actor_snapshots"."auditEventId") IN(@First,
                @Second)
            """,
                new { First = first, Second = second }));
    }

    [Fact]
    public async Task User_administration_requires_real_name_fields_persists_them_and_keeps_access_guards()
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
                WHERE "resolved1"."publicId" = @Admin),
            "roles"."id"
            FROM "identity"."roles" AS "roles"
            WHERE "roles"."code" = 'super_admin'
            """,
            new { Admin = OtherCustomer });
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CreateUser(
            OtherCustomer,
            new SaveUserRequest("new.name@test.local", "Fraud specialist", "fraud_agent", "TestPassword!2026"),
            "not-used",
            ct));
        var id = await _repository.CreateUser(
            OtherCustomer,
            new SaveUserRequest(
                "new.name@test.local",
                "A role title is not a personal name",
                "fraud_agent",
                "TestPassword!2026",
                "Thandi",
                "Ndlovu",
                "Nomsa"),
            "not-used",
            ct);
        var user = Assert.Single(await _repository.GetStaff(OtherCustomer, true, ct), u => u.Id == id);
        Assert.Equal("Thandi", user.FirstName);
        Assert.Equal("Nomsa", user.MiddleName);
        Assert.Equal("Ndlovu", user.LastName);
        Assert.Equal("Thandi Ndlovu", user.DisplayName);
        await _repository.UpdateUser(OtherCustomer, id, new UpdateUserRequest("fraud_agent", "Active", 5, "Thandi", "Dlamini", "Nomsa"), ct);
        Assert.Equal(
            "Dlamini",
            await c.ExecuteScalarAsync<string>(
                """
            SELECT "customer_profiles"."lastName" AS "last_name"
            FROM "identity"."customerProfiles" AS "customer_profiles"
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "customer_profiles"."userId") = @Id
            """,
                new { Id = id }));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.UpdateUser(
            OtherCustomer,
            OtherCustomer,
            new UpdateUserRequest("customer", "Disabled", 5, "Wrong", "Access"),
            ct));
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.UpdateUser(
            OtherCustomer,
            id,
            new UpdateUserRequest("fraud_agent", "Active", 5, "<script>", "Name"),
            ct));
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "audit"."auditEvents" AS "admin_audit_logs"
            WHERE "admin_audit_logs"."action" = 'UserNameChanged'
            AND "admin_audit_logs"."entityPublicId" = @Id
            """,
                new { Id = id }));
    }
}
