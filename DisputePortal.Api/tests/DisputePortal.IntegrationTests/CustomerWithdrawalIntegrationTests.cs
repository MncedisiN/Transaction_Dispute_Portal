using Dapper;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Service.Disputes;
using DisputePortal.Api.Models.Requests;
using Xunit;
using DisputePortal.Api.Models.Domain;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Theory]
    [InlineData("Submitted")]
    [InlineData("UnderReview")]
    [InlineData("AwaitingCustomer")]
    public async Task Customer_withdrawal_closes_every_active_stage_and_records_the_real_customer(string stage)
    {
        var ct = CancellationToken.None;
        await using var c = await _connections.OpenConnection(ct);
        await c.ExecuteAsync(
            """
            UPDATE "identity"."customerProfiles"
            SET "firstName" = 'Naledi',
            "lastName" = 'Mokoena'
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "customerProfiles"."userId") = @Customer
            """,
            new { Customer });
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "I did not make this transaction",
                Description = "Please investigate this transaction."
            },
            "withdraw-active-case",
            ct);
        var thread = await _repository.SendMessage(
            Customer,
            new SupportSendRequest(null, created.Id, "Payment question", "Please review this payment.", Transaction),
            ct);
        var currentVersion = (await _repository.GetCustomerDispute(Customer, created.Id, ct))!.Dispute.Version;
        if (stage != "Submitted")
        {
            currentVersion = await _repository.TransitionDispute(
                Agent,
                created.Id,
                new TransitionCommand(currentVersion, "UnderReview", "The specialist started the review."),
                "withdraw-stage",
                ct);
        }

        if (stage == "AwaitingCustomer")
        {
            await _repository.TransitionDispute(
                Agent,
                created.Id,
                new TransitionCommand(currentVersion, "AwaitingCustomer", "Please share additional details."),
                "withdraw-stage",
                ct);
        }

        var before = (await _repository.GetCustomerDispute(Customer, created.Id, ct))!;
        var version = await _repository.WithdrawCustomerDispute(Customer, created.Id, before.Dispute.Version, "withdraw-test", ct);
        var after = (await _repository.GetCustomerDispute(Customer, created.Id, ct))!;
        Assert.Equal("Withdrawn", after.Dispute.Status);
        Assert.Equal(before.Dispute.Version + 1, version);
        Assert.Equal(version, after.Dispute.Version);
        var history = Assert.Single(after.History, row => row.NewStatus == "Withdrawn");
        Assert.Equal(stage, history.PreviousStatus);
        Assert.Equal("Naledi Mokoena", history.ActorName);
        Assert.Equal("Naledi", history.ActorIdentity!.FirstName);
        Assert.Equal("Mokoena", history.ActorIdentity.LastName);
        Assert.Null(history.ActorIdentity.UserId);
        var operations = await _repository.GetCase(Customer, created.Id, ct);
        Assert.Equal("Withdrawn", operations.Status);
        Assert.NotNull(operations.SlaClosedAt);
        var transaction = (await _repository.GetTransaction(Customer, Transaction, ct))!;
        Assert.False(transaction.HasActiveDispute);
        Assert.Equal("Withdrawn", transaction.DisputeStatus);
        Assert.Equal("Posted", transaction.Status);
        Assert.Equal(100m, transaction.Amount);
        Assert.Equal("Withdrawn", (await _repository.GetSupportThread(Customer, thread, ct))!.CaseStatus);
        Assert.Single(await _repository.GetMessages(Customer, thread, ct));
        var activity = await Finance().GetCaseActivity(Customer, created.Id, ct);
        Assert.Contains(activity, row => row.Action == "Withdrawn" && row.Actor == "Naledi Mokoena");
        Assert.True(await c.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM "notifications"."deliveries" AS "notification_deliveries"
            WHERE(
                SELECT "related1"."publicId"
                FROM "disputes"."disputes" AS "related1"
                WHERE "related1"."id" = "notification_deliveries"."disputeId") = @Id
            AND "notification_deliveries"."milestone" = 'Completed'
            """,
            new { Id = created.Id }) > 0);
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "audit"."auditEvents" AS "admin_audit_logs"
            WHERE "admin_audit_logs"."entityPublicId" = @Id
            AND "admin_audit_logs"."action" = 'DisputeWithdrawn'
            """,
                new { Id = created.Id }));
    }

    [Fact]
    public async Task Withdrawal_rejects_unowned_stale_and_repeated_requests_without_changing_history()
    {
        var ct = CancellationToken.None;
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "I did not make this transaction",
                Description = "Please investigate this transaction."
            },
            "withdraw-security",
            ct);
        var initial = (await _repository.GetCustomerDispute(Customer, created.Id, ct))!;
        var initialVersion = initial.Dispute.Version;
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.WithdrawCustomerDispute(
            OtherCustomer,
            created.Id,
            initialVersion,
            "unowned-withdrawal",
            ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.WithdrawCustomerDispute(
            Customer,
            created.Id,
            initialVersion + 1,
            "stale-withdrawal",
            ct));
        var unchanged = (await _repository.GetCustomerDispute(Customer, created.Id, ct))!;
        Assert.Equal("Submitted", unchanged.Dispute.Status);
        Assert.Equal(initialVersion, unchanged.Dispute.Version);
        Assert.Equal(initial.History.Count, unchanged.History.Count);
        var withdrawnVersion = await _repository.WithdrawCustomerDispute(Customer, created.Id, initialVersion, "valid-withdrawal", ct);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.WithdrawCustomerDispute(
            Customer,
            created.Id,
            withdrawnVersion,
            "repeat-withdrawal",
            ct));
        var withdrawn = (await _repository.GetCustomerDispute(Customer, created.Id, ct))!;
        Assert.Equal(initialVersion + 1, withdrawn.Dispute.Version);
        Assert.Equal(initial.History.Count + 1, withdrawn.History.Count);
    }

    [Fact]
    public async Task Concurrent_customer_withdrawals_commit_only_once()
    {
        var ct = CancellationToken.None;
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "I did not make this transaction",
                Description = "Please investigate this transaction."
            },
            "withdraw-concurrent",
            ct);
        var initialVersion = (await _repository.GetCustomerDispute(Customer, created.Id, ct))!.Dispute.Version;
        async Task<bool> Attempt()
        {
            try
            {
                await _repository.WithdrawCustomerDispute(Customer, created.Id, initialVersion, "concurrent-withdrawal", ct);
                return true;
            }
            catch (PortalConflictException)
            {
                return false;
            }
        }

        var outcomes = await Task.WhenAll(Attempt(), Attempt());
        Assert.Single(outcomes, succeeded => succeeded);
        Assert.Single(outcomes, succeeded => !succeeded);
        var after = (await _repository.GetCustomerDispute(Customer, created.Id, ct))!;
        Assert.Equal(initialVersion + 1, after.Dispute.Version);
        Assert.Single(after.History, row => row.NewStatus == "Withdrawn");
    }

    [Theory]
    [InlineData("Resolved")]
    [InlineData("Declined")]
    [InlineData("Withdrawn")]
    public async Task Closed_cases_reject_messages_from_customer_specialist_and_supervisor_through_every_entry_path(string status)
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
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "I did not make this transaction",
                Description = "Please investigate this transaction."
            },
            "closed-message-guard",
            ct);
        var initial = new SupportSendRequest(
            null,
            created.Id,
            "Payment question",
            "Please review this payment.",
            Transaction,
            Guid.NewGuid());
        var thread = await _repository.SendMessage(Customer, initial, ct);
        var currentVersion = (await _repository.GetCustomerDispute(Customer, created.Id, ct))!.Dispute.Version;
        if (status == "Withdrawn")
        {
            await _repository.WithdrawCustomerDispute(Customer, created.Id, currentVersion, "close-for-message-test", ct);
        }
        else
        {
            currentVersion = await _repository.TransitionDispute(
                Agent,
                created.Id,
                new TransitionCommand(currentVersion, "UnderReview", "The specialist started the review."),
                "close-for-message-test",
                ct);
            await _repository.TransitionDispute(
                Agent,
                created.Id,
                new TransitionCommand(currentVersion, status, "The specialist completed the review."),
                "close-for-message-test",
                ct);
        }

        foreach (var actor in new[]
        {
            Customer,
            Agent,
            OtherCustomer
        }

        )
        {
            await Assert.ThrowsAsync<PortalConflictException>(() => _repository.SendMessage(
                actor,
                new SupportSendRequest(thread, created.Id, "Payment question", "A late case message.", Transaction),
                ct));
            await Assert.ThrowsAsync<PortalConflictException>(() => _repository.SendMessage(actor, new SupportSendRequest(
                null,
                created.Id,
                "Payment question",
                "A late case message."), ct));
        }

        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.SendMessage(
            Customer,
            new SupportSendRequest(null, null, "Payment question", "A late transaction message.", Transaction),
            ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.SendMessage(Customer, initial, ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.AddNote(
            Agent,
            created.Id,
            new NoteCommand("A late customer update.", "Customer"),
            "closed-note",
            ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.AddNote(
            OtherCustomer,
            created.Id,
            new NoteCommand("A late internal update.", "Internal"),
            "closed-note",
            ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.EnsureEvidenceUploadAllowed(Customer, created.Id, ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.EnsureEvidenceUploadAllowed(Agent, created.Id, ct));
        Assert.Single(await _repository.GetMessages(Customer, thread, ct));
        Assert.Equal(status, (await _repository.GetCustomerDispute(Customer, created.Id, ct))!.Dispute.Status);
    }

    [Fact]
    public async Task Resolver_is_the_recorded_terminal_actor_and_is_cleared_when_the_case_reopens()
    {
        var ct = CancellationToken.None;
        await using var c = await _connections.OpenConnection(ct);
        await c.ExecuteAsync(
            """
            UPDATE "identity"."customerProfiles"
            SET "firstName" = 'Kabelo',
            "lastName" = 'Dube'
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "customerProfiles"."userId") = @Agent;
            UPDATE "identity"."customerProfiles"
            SET "firstName" = 'Lindiwe',
            "lastName" = 'Maseko'
            WHERE(
                SELECT "related2"."publicId"
                FROM "identity"."users" AS "related2"
                WHERE "related2"."id" = "customerProfiles"."userId") = @Admin;
            INSERT INTO "identity"."userRoles"("userId",
                "roleId")
            SELECT(
                SELECT "resolved3"."id"
                FROM "identity"."users" AS "resolved3"
                WHERE "resolved3"."publicId" = @Admin),
            "roles"."id"
            FROM "identity"."roles" AS "roles"
            WHERE "roles"."code" = 'super_admin'
            """,
            new { Agent, Admin = OtherCustomer });
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "I did not make this transaction",
                Description = "Please investigate this transaction."
            },
            "actual-resolver",
            ct);
        var initialCase = await _repository.GetCase(Customer, created.Id, ct);
        Assert.Equal(Agent, initialCase.AssignedTo);
        var currentVersion = await _repository.TransitionDispute(
            OtherCustomer,
            created.Id,
            new TransitionCommand(initialCase.Version, "UnderReview", "Supervisor started the review."),
            "actual-resolver",
            ct);
        currentVersion = await _repository.TransitionDispute(
            OtherCustomer,
            created.Id,
            new TransitionCommand(currentVersion, "Resolved", "Supervisor completed the investigation."),
            "actual-resolver",
            ct);
        var customer = (await _repository.GetCustomerDispute(Customer, created.Id, ct))!;
        var staff = (await _repository.GetAdminDispute(created.Id, ct, OtherCustomer))!;
        Assert.Equal("Lindiwe Maseko", customer.ResolvedBy!.Label);
        Assert.Null(customer.ResolvedBy.UserId);
        Assert.Null(customer.ResolvedBy.LoginIdentifier);
        Assert.Empty(customer.ResolvedBy.Roles);
        Assert.Null(customer.ResolvedBy.Provenance);
        Assert.Equal("Lindiwe Maseko", customer.ResolvedBy.AccountName);
        using (var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(
            customer,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))))
        {
            var resolver = json.RootElement.GetProperty("resolvedBy");
            Assert.False(resolver.TryGetProperty("provenance", out _));
            Assert.Equal("Lindiwe", resolver.GetProperty("firstName").GetString());
            Assert.Equal("Maseko", resolver.GetProperty("lastName").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, resolver.GetProperty("userId").ValueKind);
            Assert.Equal(System.Text.Json.JsonValueKind.Null, resolver.GetProperty("loginIdentifier").ValueKind);
            Assert.Equal(0, resolver.GetProperty("roles").GetArrayLength());
        }

        Assert.Equal(OtherCustomer, staff.ResolvedBy!.UserId);
        Assert.Equal("Lindiwe Maseko", staff.ResolvedBy.Label);
        Assert.Equal("Recorded", staff.ResolvedBy.Provenance);
        await _repository.TransitionDispute(
            OtherCustomer,
            created.Id,
            new TransitionCommand(currentVersion, "UnderReview", "New evidence requires a fresh investigation."),
            "actual-resolver",
            ct);
        var reopened = (await _repository.GetCustomerDispute(Customer, created.Id, ct))!;
        Assert.Null(reopened.ResolvedBy);
        Assert.Null((await _repository.GetAdminDispute(created.Id, ct, OtherCustomer))!.ResolvedBy);
        await _repository.TransitionDispute(
            Agent,
            created.Id,
            new TransitionCommand(reopened.Dispute.Version, "Resolved", "Specialist resolved the reopened investigation."),
            "actual-resolver",
            ct);
        Assert.Equal("Kabelo Dube", (await _repository.GetCustomerDispute(Customer, created.Id, ct))!.ResolvedBy!.Label);
        await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(() => c.ExecuteAsync(
            """
            UPDATE disputes."statusHistory" SET "changedAt"=@At
            WHERE "disputeId"=(SELECT id FROM disputes.disputes WHERE "publicId"=@Id)
            """,
            new { Id = created.Id, At = DateTimeOffset.UtcNow }));
        // Append equal-time fixture events instead of rewriting retained history.
        // Public reads must select the later internal identity deterministically.
        var sameTime = DateTimeOffset.UtcNow;
        foreach (var resolver in new[]
        {
            OtherCustomer,
            Agent
        }

        )
            await c.ExecuteAsync(
            """
                INSERT INTO disputes."statusHistory"
                    ("disputeId","actorUserId","publicId","previousStatus","newStatus","changedAt",comment)
                VALUES (
                    (SELECT id FROM disputes.disputes WHERE "publicId"=@Id),
                    (SELECT id FROM "identity".users WHERE "publicId"=@Actor),
                    @Event,'UnderReview','Resolved',@At,'Equal-time resolver fixture.')
                """,
            new
            {
                Id = created.Id,
                Actor = resolver,
                Event = Guid.NewGuid(),
                At = sameTime
            });
        Assert.Equal("Kabelo Dube", (await _repository.GetCustomerDispute(Customer, created.Id, ct))!.ResolvedBy!.Label);
    }
}
