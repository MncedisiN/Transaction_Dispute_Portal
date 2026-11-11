using DisputePortal.TestFixtures;
using Microsoft.Extensions.Logging;
using Dapper;
using System.Data.Common;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Service.Disputes;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Repository;
using DisputePortal.Api.Repository.Data;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Debit_order_is_routed_to_specialist_and_manually_refunded_exactly_once()
    {
        var ct = CancellationToken.None;
        await using var c = await _connections.OpenConnection(ct);
        await c.ExecuteAsync(
            """
            UPDATE "transactions"."transactions"
            SET "transactionTypeId" =(
                SELECT "resolved1"."id"
                FROM "configuration"."transactionTypes" AS "resolved1"
                WHERE "resolved1"."code" = 'DebitOrder')
            WHERE "transactions"."publicId" = @Transaction;
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
            WHERE "roles"."code" = 'debit_order_specialist'
            """,
            new { Transaction, Agent });
        var result = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Duplicate",
                Reason = "I was charged more than once",
                Description = "Please review this debit order."
            },
            "debit-order-review",
            ct);
        var finance = Finance();
        var before = Assert.Single(await finance.GetBalances(Customer, ct));
        Assert.True(await finance.ProcessAutomatic(ct));
        var decision = await finance.GetCaseFinancials(Customer, result.Id, ct);
        Assert.Equal("ManualReview", decision.Decision);
        Assert.Null(decision.Refund);
        var assigned = await _repository.GetCase(Agent, result.Id, ct);
        Assert.Equal("DebitOrder", assigned.QueueType);
        Assert.Equal(Agent, assigned.AssignedTo);
        var version = await _repository.TransitionDispute(
            Agent,
            result.Id,
            new TransitionCommand(assigned.Version, "UnderReview", "Debit order reviewed by qualified specialist."),
            "debit-review",
            ct);
        var request = new ApproveRefundRequest(version, Guid.NewGuid(), "Verified debit order; full refund approved.");
        var refund = await finance.ApproveRefund(Agent, result.Id, request, "debit-refund", ct);
        var replay = await finance.ApproveRefund(Agent, result.Id, request, "debit-refund", ct);
        Assert.Equal(refund.CreditTransactionId, replay.CreditTransactionId);
        Assert.True(replay.Replayed);
        Assert.Equal(before.CurrentBalance + 100m, Assert.Single(await finance.GetBalances(Customer, ct)).CurrentBalance);
        Assert.Equal("Resolved", (await _repository.GetCustomerDispute(Customer, result.Id, ct))!.Dispute.Status);
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "disputes"."refunds" AS "dispute_refunds"
            WHERE(
                SELECT "related1"."publicId"
                FROM "disputes"."disputes" AS "related1"
                WHERE "related1"."id" = "dispute_refunds"."disputeId") = @Id
            """,
                new { Id = result.Id }));
    }

    private FinanceRepository Finance() => new(
        _connections,
        DatabaseDialect.GetDatabaseDialect(_provider),
        TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"),
        TestRuntimeConfiguration.Read(),
        new CaseServicePolicy());

    private async Task<(Guid Id, int Version)> ReviewableRefundCase()
    {
        var ct = CancellationToken.None;
        var result = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "I did not make this transaction",
                Description = "Please investigate this unauthorised card purchase."
            },
            "refund-test-" + Guid.NewGuid(),
            ct);
        var detail = await _repository.GetCustomerDispute(Customer, result.Id, ct);
        var version = await _repository.TransitionDispute(
            Agent,
            result.Id,
            new TransitionCommand(detail!.Dispute.Version, "UnderReview", "Review started."),
            "finance-test",
            ct);
        return (result.Id, version);
    }

    [Fact]
    public async Task Refund_posting_is_atomic_owned_and_replayed_exactly_once_under_concurrency()
    {
        var ct = CancellationToken.None;
        var finance = Finance();
        var reviewed = await ReviewableRefundCase();
        var before = Assert.Single(await finance.GetBalances(Customer, ct), x => x.AccountId == Account);
        var request = new ApproveRefundRequest(reviewed.Version, Guid.NewGuid(), "Original payment verified and full refund approved.");
        var results = await Task.WhenAll(
            finance.ApproveRefund(Agent, reviewed.Id, request, "refund-test", ct),
            finance.ApproveRefund(Agent, reviewed.Id, request, "refund-test", ct));
        Assert.Equal(results[0].CreditTransactionId, results[1].CreditTransactionId);
        Assert.Single(results, x => x.Replayed);
        Assert.Equal(results[0].CustomerReference, results[1].CustomerReference);
        Assert.Matches("^RF-[0-9]{8}$", results[0].CustomerReference!);
        var after = Assert.Single(await finance.GetBalances(Customer, ct), x => x.AccountId == Account);
        Assert.Equal(before.CurrentBalance + 100m, after.CurrentBalance);
        var overview = await finance.GetOverview(Customer, ct);
        Assert.Equal(after.CurrentBalance, overview.CurrentBalance);
        Assert.Equal(1, overview.AccountCount);
        Assert.Equal("Resolved", (await _repository.GetCustomerDispute(Customer, reviewed.Id, ct))!.Dispute.Status);
        await using var c = await _connections.OpenConnection(ct);
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "disputes"."refunds" AS "dispute_refunds"
            WHERE(
                SELECT "related1"."publicId"
                FROM "disputes"."disputes" AS "related1"
                WHERE "related1"."id" = "dispute_refunds"."disputeId") = @Id
            """,
                new { reviewed.Id }));
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "transactions"."transactions" AS "transactions"
            WHERE(
                SELECT "related1"."code"
                FROM "configuration"."transactionTypes" AS "related1"
                WHERE "related1"."id" = "transactions"."transactionTypeId") = 'Refund'
            AND(
                SELECT "related2"."publicId"
                FROM "accounts"."accounts" AS "related2"
                WHERE "related2"."id" = "transactions"."accountId") = @Account
            """,
                new { Account }));
        Assert.Equal(
            100m,
            await c.ExecuteScalarAsync<decimal>(
                """
            SELECT SUM("financial_ledger"."currentDelta")
            FROM "ledger"."projectionEntries" AS "financial_ledger"
            WHERE(
                SELECT "related1"."publicId"
                FROM "transactions"."transactions" AS "related1"
                WHERE "related1"."id" = "financial_ledger"."transactionId") = @Id
            """,
                new { Id = results[0].CreditTransactionId }));
        await Assert.ThrowsAsync<PortalConflictException>(() => finance.ApproveRefund(
            Agent,
            reviewed.Id,
            new ApproveRefundRequest(request) { RequestId = Guid.NewGuid() },
            "repeat",
            ct));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => finance.GetCaseFinancials(OtherCustomer, reviewed.Id, ct));
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync(
            """
            UPDATE "transactions"."transactions"
            SET "transactionStatus" = 'Reversed'
            WHERE "transactions"."publicId" = @Id
            """,
            new { Id = Transaction }));
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync(
            """
            UPDATE "ledger"."projectionEntries"
            SET "currentDelta" = 999
            WHERE(
                SELECT "related1"."publicId"
                FROM "accounts"."accounts" AS "related1"
                WHERE "related1"."id" = "projectionEntries"."accountId") = @Account
            """,
            new { Account }));
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync(
            """
            UPDATE "disputes"."refunds"
            SET "amount" = 999
            WHERE(
                SELECT "related1"."publicId"
                FROM "disputes"."disputes" AS "related1"
                WHERE "related1"."id" = "refunds"."disputeId") = @Id
            """,
            new { reviewed.Id }));
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync(
            """
            UPDATE "disputes"."refundReceipts"
            SET "customerReference" = 'RF-99999999'
            WHERE(
                SELECT(
                    SELECT "related2"."publicId"
                    FROM "disputes"."disputes" AS "related2"
                    WHERE "related2"."id" = "related1"."disputeId")
                FROM "disputes"."refunds" AS "related1"
                WHERE "related1"."id" = "refundReceipts"."refundId") = @Id
            """,
            new { reviewed.Id }));
    }

    [Fact]
    public async Task Ledger_tracks_pending_settlement_and_reversal_without_mutating_old_entries()
    {
        var ct = CancellationToken.None;
        var id = Guid.NewGuid();
        var finance = Finance();
        await using var c = await _connections.OpenConnection(ct);
        await c.ExecuteAsync(
            """
            INSERT INTO "transactions"."transactions"("publicId",
                "accountId",
                "transactionReference",
                "transactionAt",
                "description",
                "merchantName",
                "amount",
                "currencyCode",
                "transactionTypeId",
                "direction",
                "transactionStatus",
                "disputeEligible")
            VALUES(@Id,
                (
                    SELECT "resolved1"."id"
                    FROM "accounts"."accounts" AS "resolved1"
                    WHERE "resolved1"."publicId" = @Account),
                @Ref,
                @Now,
                'Pending payment',
                'Example',
                20,
                'ZAR',
                (
                    SELECT "resolved2"."id"
                    FROM "configuration"."transactionTypes" AS "resolved2"
                    WHERE "resolved2"."code" = 'CardPurchase'),
                'Debit',
                'Pending',
                @No)
            """,
            new
            {
                Id = id,
                Account,
                Ref = id.ToString(),
                Now = DateTimeOffset.UtcNow,
                No = false
            });
        var pending = Assert.Single(await finance.GetBalances(Customer, ct));
        Assert.Equal(-100m, pending.CurrentBalance);
        Assert.Equal(20m, pending.ReservedAmount);
        Assert.Equal(-120m, pending.AvailableBalance);
        await c.ExecuteAsync(
            """
            UPDATE "transactions"."transactions"
            SET "transactionStatus" = 'Posted'
            WHERE "transactions"."publicId" = @Id
            """,
            new { Id = id });
        var posted = Assert.Single(await finance.GetBalances(Customer, ct));
        Assert.Equal(-120m, posted.CurrentBalance);
        Assert.Equal(0m, posted.ReservedAmount);
        await c.ExecuteAsync(
            """
            UPDATE "transactions"."transactions"
            SET "transactionStatus" = 'Reversed'
            WHERE "transactions"."publicId" = @Id
            """,
            new { Id = id });
        Assert.Equal(-100m, Assert.Single(await finance.GetBalances(Customer, ct)).CurrentBalance);
        Assert.Equal(
            5,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "ledger"."projectionEntries" AS "financial_ledger"
            WHERE(
                SELECT "related1"."publicId"
                FROM "transactions"."transactions" AS "related1"
                WHERE "related1"."id" = "financial_ledger"."transactionId") = @Id
            """,
                new { Id = id }));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => finance.GetBalanceHistory(OtherCustomer, Account, ct));
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync(
            """
            UPDATE "accounts"."accounts"
            SET "currencyCode" = 'USD'
            WHERE "accounts"."publicId" = @Account
            """,
            new { Account }));
    }

    [Fact]
    public async Task Ineligible_refund_leaves_no_credit_or_partial_resolution_and_records_failure()
    {
        var ct = CancellationToken.None;
        var finance = Finance();
        var reviewed = await ReviewableRefundCase();
        await using var c = await _connections.OpenConnection(ct);
        await c.ExecuteAsync(
            """
            UPDATE "transactions"."transactions"
            SET "transactionStatus" = 'Reversed'
            WHERE "transactions"."publicId" = @Id
            """,
            new { Id = Transaction });
        await Assert.ThrowsAsync<PortalConflictException>(() => finance.ApproveRefund(
            Agent,
            reviewed.Id,
            new(reviewed.Version, Guid.NewGuid(), "Payment already reversed; do not credit it again."),
            "reversal-test",
            ct));
        Assert.Equal(
            0,
            await c.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "disputes"."refunds" AS "dispute_refunds"
            """));
        Assert.Equal("UnderReview", (await _repository.GetCustomerDispute(Customer, reviewed.Id, ct))!.Dispute.Status);
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "audit"."auditEvents" AS "admin_audit_logs"
            WHERE "admin_audit_logs"."action" = 'RefundFailed'
            AND "admin_audit_logs"."entityPublicId" = @Id
            """,
                new { reviewed.Id }));
    }

    [Fact]
    public async Task Trusted_duplicate_is_automatically_refunded_with_system_actor_and_no_reprocessing()
    {
        var ct = CancellationToken.None;
        await VerifyProvisionedContact(Customer, "customer@test.local");
        var account = await _repository.OpenAccount(Customer, Guid.NewGuid(), "Savings", 10, ct);
        await _repository.ProcessNextTransactionBatch(ct);
        await using var c = await _connections.OpenConnection(ct);
        var transaction = await c.ExecuteScalarAsync<Guid>(
            """
            SELECT(
                SELECT "related1"."publicId"
                FROM "transactions"."transactions" AS "related1"
                WHERE "related1"."id" = "p"."transactionId") AS "transaction_id"
            FROM "transactions"."duplicateConfirmations" p
            JOIN "transactions"."transactions" t
            ON "t"."id" = "p"."transactionId"
            WHERE(
                SELECT "related2"."publicId"
                FROM "accounts"."accounts" AS "related2"
                WHERE "related2"."id" = "t"."accountId") = @Id
            """,
            new { Id = account.AccountId });
        var result = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = transaction,
                Category = "Duplicate",
                Reason = "I was charged more than once",
                Description = "This appears to be a repeated card payment."
            },
            "auto-refund-" + Guid.NewGuid(),
            ct);
        var finance = Finance();
        var before = (await finance.GetBalances(Customer, ct)).Single(x => x.AccountId == account.AccountId);
        Assert.Equal("Submitted", (await _repository.GetCustomerDispute(Customer, result.Id, ct))!.Dispute.Status);
        var processors = await Task.WhenAll(finance.ProcessAutomatic(ct), finance.ProcessAutomatic(ct));
        Assert.Single(processors, value => value);
        Assert.False(await finance.ProcessAutomatic(ct));
        var summary = await finance.GetCaseFinancials(Customer, result.Id, ct);
        Assert.True(summary.Refund!.Automatic);
        Assert.Equal(79.90m, summary.Refund.Amount);
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "disputes"."refunds" AS "dispute_refunds"
            WHERE(
                SELECT "related1"."publicId"
                FROM "disputes"."disputes" AS "related1"
                WHERE "related1"."id" = "dispute_refunds"."disputeId") = @Id
            """,
                new { result.Id }));
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "transactions"."transactions" AS "transactions"
            WHERE "transactions"."publicId" = @Id
            AND(
                SELECT "related1"."code"
                FROM "configuration"."transactionTypes" AS "related1"
                WHERE "related1"."id" = "transactions"."transactionTypeId") = 'Refund'
            """,
                new { Id = summary.Refund.CreditTransactionId }));
        Assert.Equal(AutomaticRefundPolicy.RuleCode, summary.RuleCode);
        Assert.Equal(
            before.CurrentBalance + 79.90m,
            (await finance.GetBalances(Customer, ct)).Single(x => x.AccountId == account.AccountId).CurrentBalance);
        var detail = await _repository.GetCustomerDispute(Customer, result.Id, ct);
        Assert.Equal("Resolved", detail!.Dispute.Status);
        Assert.All(detail.History.Where(x => x.NewStatus is "UnderReview" or "Resolved"), x => Assert.Equal("System", x.ActorName));
        Assert.DoesNotContain(await finance.GetCaseActivity(Customer, result.Id, ct), x => x.Action == "AutomaticDecision");
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
        Assert.Contains(
            await finance.GetCaseActivity(OtherCustomer, result.Id, ct),
            x => x.Action == "AutomaticDecision" && x.Actor == "System");
    }

    [Fact]
    public async Task Customer_duplicate_claim_alone_never_authorises_an_automatic_credit()
    {
        var ct = CancellationToken.None;
        var result = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Duplicate",
                Reason = "I was charged more than once",
                Description = "Please check whether this payment was duplicated."
            },
            "no-proof-" + Guid.NewGuid(),
            ct);
        var finance = Finance();
        Assert.True(await finance.ProcessAutomatic(ct));
        var summary = await finance.GetCaseFinancials(Customer, result.Id, ct);
        Assert.Null(summary.Refund);
        Assert.Equal("ManualReview", summary.Decision);
        Assert.Equal("Submitted", (await _repository.GetCustomerDispute(Customer, result.Id, ct))!.Dispute.Status);
    }

    [Fact]
    public async Task Password_change_compare_and_swap_revokes_all_sessions_and_rejects_stale_hash()
    {
        var ct = CancellationToken.None;
        var first = await _repository.CreateSession(Customer, DateTimeOffset.UtcNow.AddMinutes(20), ct);
        var second = await _repository.CreateSession(Customer, DateTimeOffset.UtcNow.AddMinutes(20), ct);
        var auth = new AuthenticationRepository(_connections, DatabaseDialect.GetDatabaseDialect(_provider));
        await auth.ChangePassword(Customer, "not-used", "new-test-hash", ct);
        Assert.False(await _repository.IsSessionActive(first, DateTimeOffset.UtcNow, ct));
        Assert.False(await _repository.IsSessionActive(second, DateTimeOffset.UtcNow, ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => auth.ChangePassword(Customer, "not-used", "stale-hash", ct));
        Assert.Equal("new-test-hash", (await auth.GetPasswordState(Customer, ct))!.PasswordHash);
    }

    [Fact]
    public async Task Support_message_retries_are_idempotent_and_history_pages_do_not_overlap()
    {
        var ct = CancellationToken.None;
        var key = Guid.NewGuid();
        var request = new SupportSendRequest(null, null, "Account enquiry", "Please help with this transaction.", Transaction, key);
        var first = await _repository.SendMessage(Customer, request, ct);
        var replay = await _repository.SendMessage(Customer, request, ct);
        Assert.Equal(first, replay);
        Assert.Single(await _repository.GetMessages(Customer, first, ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.SendMessage(
            Customer,
            new SupportSendRequest(request) { Body = "Different content with the same request reference." },
            ct));
        await using var c = await _connections.OpenConnection(ct);
        for (var i = 0; i < 205; i++)
            await c.ExecuteAsync(
            """
                INSERT INTO "support"."messages"("publicId",
                    "threadId",
                    "authorId",
                    "body")
                VALUES(@Id,
                    (
                        SELECT "resolved1"."id"
                        FROM "support"."conversations" AS "resolved1"
                        WHERE "resolved1"."publicId" = @Thread),
                    (
                        SELECT "resolved2"."id"
                        FROM "identity"."users" AS "resolved2"
                        WHERE "resolved2"."publicId" = @Actor),
                    @Body)
                """,
            new
            {
                Id = Guid.NewGuid(),
                Thread = first,
                Actor = Customer,
                Body = "History message " + i
            });
        var latest = await _repository.GetMessages(Customer, first, ct);
        Assert.Equal(200, latest.Count);
        var older = await _repository.GetMessages(Customer, first, ct, beforeSequence: latest[0].Sequence);
        Assert.Equal(6, older.Count);
        Assert.True(older[^1].Sequence < latest[0].Sequence);
        var latestPage = await _repository.GetMessagePage(Customer, first, ct);
        Assert.True(latestPage.HasMore);
        Assert.Equal(latest.Select(message => message.Id), latestPage.Items.Select(message => message.Id));
        var olderPage = await _repository.GetMessagePage(Customer, first, ct, beforeSequence: latest[0].Sequence);
        Assert.False(olderPage.HasMore);
        Assert.Equal(older.Select(message => message.Id), olderPage.Items.Select(message => message.Id));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.GetMessages(
            OtherCustomer,
            first,
            ct,
            beforeSequence: latest[0].Sequence));
    }

    [Fact]
    public async Task Reopening_requires_a_supervisor_and_cannot_repeat_a_refunded_financial_workflow()
    {
        var ct = CancellationToken.None;
        var item = await ReviewableRefundCase();
        var closed = await _repository.TransitionDispute(
            Agent,
            item.Id,
            new(item.Version, "Resolved", "Review completed without any financial credit."),
            "close-test",
            ct);
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.TransitionDispute(
            Agent,
            item.Id,
            new(closed, "UnderReview", "New evidence requires a further review."),
            "reopen-test",
            ct));
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
            WHERE "roles"."code" = 'super_admin'
            """,
            new { Id = Agent });
        await _repository.TransitionDispute(
            Agent,
            item.Id,
            new(closed, "UnderReview", "New evidence requires a further review."),
            "reopen-test",
            ct);
        var open = await _repository.GetCustomerDispute(Customer, item.Id, ct);
        Assert.Equal("UnderReview", open!.Dispute.Status);
        Assert.Null(open.Dispute.Resolution);
        await Finance().ApproveRefund(
            Agent,
            item.Id,
            new(open.Dispute.Version, Guid.NewGuid(), "Further evidence supports the full eligible refund."),
            "approve-test",
            ct);
        var refunded = await _repository.GetCustomerDispute(Customer, item.Id, ct);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.TransitionDispute(
            Agent,
            item.Id,
            new(refunded!.Dispute.Version, "UnderReview", "Attempting to repeat the financial workflow."),
            "repeat-test",
            ct));
    }

    [Fact]
    public async Task Optimized_evidence_preserves_the_original_under_the_same_document_grant()
    {
        var ct = CancellationToken.None;
        var item = await ReviewableRefundCase();
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jhLcAAAAASUVORK5CYII=");
        // Retained original bytes use the same document authorisation as the optimized upload.
        var evidence = new EvidenceRepository(_connections, DatabaseDialect.GetDatabaseDialect(_provider), new TestProtector(), new EvidencePolicyRepository(_connections), DisputePortal.TestFixtures.TestRuntimeConfiguration.Read());
        var id = await evidence.AddEvidence(
            Customer,
            item.Id,
            "photo.png",
            png,
            "Scanned",
            "PhotoOrScreenshot",
            ct,
            new("original.png", png));
        Assert.True(Assert.Single(await evidence.GetEvidence(Customer, item.Id, ct)).HasOriginal);
        Assert.Equal(png, (await evidence.DownloadOriginalEvidence(Customer, item.Id, id, ct)).Content);
        await Assert.ThrowsAsync<PortalNotFoundException>(() => evidence.DownloadOriginalEvidence(OtherCustomer, item.Id, id, ct));
        await Assert.ThrowsAsync<PortalForbiddenException>(() => evidence.DownloadOriginalEvidence(Agent, item.Id, id, ct));
        await using var c = await _connections.OpenConnection(ct);
        Assert.Equal(
            "binary:v1",
            await c.ExecuteScalarAsync<string>(
                """
            SELECT "dispute_evidence"."protectedOriginal" AS "protected_original"
            FROM "disputes"."evidence" AS "dispute_evidence"
            WHERE "dispute_evidence"."publicId" = @Id
            """,
                new { Id = id }));
        Assert.NotNull(await c.ExecuteScalarAsync<byte[]>(
            """
            SELECT "b"."protectedOriginal" AS "protected_original"
            FROM "disputes"."evidencePayloads" b
            JOIN "disputes"."evidence" e
            ON "e"."payloadId" = "b"."id"
            WHERE "e"."publicId" = @Id
            """,
            new { Id = id }));
    }

    [Fact]
    public async Task Recovery_cannot_reuse_the_current_password_and_activity_never_exposes_internal_notes()
    {
        var ct = CancellationToken.None;
        await VerifyProvisionedContact(Customer, "customer@test.local");
        var challenge = await _repository.CreateIdentityChallenge(Customer, "Recovery", "email", "customer@test.local", "", "654321", ct);
        await _repository.MarkIdentityChallengeReady(challenge.ChallengeId, ct);
        var session = await _repository.CreateSession(Customer, DateTimeOffset.UtcNow.AddMinutes(20), ct);
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CompletePasswordReset(
            challenge.ChallengeId,
            "654321",
            "replacement",
            ct,
            hash => hash == "not-used"));
        Assert.True(await _repository.IsSessionActive(session, DateTimeOffset.UtcNow, ct));
        await _repository.CompletePasswordReset(challenge.ChallengeId, "654321", "replacement", ct, _ => false);
        Assert.False(await _repository.IsSessionActive(session, DateTimeOffset.UtcNow, ct));
        var item = await ReviewableRefundCase();
        await _repository.AddNote(Agent, item.Id, new NoteCommand("Private reviewer intelligence.", "Internal"), "activity-test", ct);
        await _repository.AddNote(Agent, item.Id, new NoteCommand("Please upload a clear receipt.", "Customer"), "activity-test", ct);
        var activity = await Finance().GetCaseActivity(Customer, item.Id, ct);
        Assert.Contains(activity, x => x.Detail == "Please upload a clear receipt.");
        Assert.DoesNotContain(activity, x => x.Detail == "Private reviewer intelligence.");
        var audit = await _repository.GetAuditLogs(1, 100, "PasswordReset", ct);
        Assert.Contains(audit.Items, x => x.Outcome == "Success" && !string.IsNullOrWhiteSpace(x.CorrelationId));
    }

    [Fact]
    public async Task Evidence_primary_only_files_count_toward_case_byte_quota()
    {
        var ct = CancellationToken.None;
        var reviewed = await ReviewableRefundCase();
        await using var c = await _connections.OpenConnection(ct);
        // Quota accounting includes committed metadata rows without an original payload.
        for (var i = 0; i < 5; i++)
            await c.ExecuteAsync(
            """
                INSERT INTO "disputes"."evidence"("publicId",
                    "disputeId",
                    "uploadedById",
                    "fileName",
                    "contentType",
                    "sizeBytes",
                    "sha256",
                    "protectedContent",
                    "scanStatus",
                    "category",
                    "categoryId",
                    "createdAt")
                VALUES(@Id,
                    (
                        SELECT "resolved1"."id"
                        FROM "disputes"."disputes" AS "resolved1"
                        WHERE "resolved1"."publicId" = @Case),
                    (
                        SELECT "resolved2"."id"
                        FROM "identity"."users" AS "resolved2"
                        WHERE "resolved2"."publicId" = @Actor),
                    @Name,
                    'application/pdf',
                    @Size,
                    @Hash,
                    'test:unused-quota-fixture',
                    'Scanned',
                    'Other',
                    (SELECT "id" FROM configuration."evidenceCategories" WHERE "code" = 'Other'),
                    @Now)
                """,
            new
            {
                Id = Guid.NewGuid(),
                Case = reviewed.Id,
                Actor = Customer,
                Name = "legacy-" + i + ".pdf",
                Size = (await ReadEvidenceSettings()).MaximumFileBytes,
                Now = DateTimeOffset.UtcNow,
                Hash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")
            });
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.AddEvidence(
            Customer,
            reviewed.Id,
            "additional.png",
            EvidencePng(),
            "Scanned",
            "Other",
            ct));
        Assert.Equal(
            5,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "disputes"."evidence" AS "dispute_evidence"
            WHERE(
                SELECT "related1"."publicId"
                FROM "disputes"."disputes" AS "related1"
                WHERE "related1"."id" = "dispute_evidence"."disputeId") = @Id
            """,
                new { Id = reviewed.Id }));
    }

    [Fact]
    public async Task Evidence_legacy_payloads_remain_readable_and_new_payloads_are_immutable()
    {
        var ct = CancellationToken.None;
        var reviewed = await ReviewableRefundCase();
        var png = EvidencePng();
        var id = await _repository.AddEvidence(
            Customer,
            reviewed.Id,
            "optimized.png",
            png,
            "Scanned",
            "PhotoOrScreenshot",
            ct,
            new("original.png", png));
        await using var c = await _connections.OpenConnection(ct);
        await Assert.ThrowsAnyAsync<DbException>(() => c.ExecuteAsync(
            """
            UPDATE "disputes"."evidencePayloads"
            SET "protectedOriginal" = "evidencePayloads"."protectedContent"
            WHERE "evidencePayloads"."publicId" = @Id
            """,
            new { Id = id }));
        var other = await _repository.CreateDispute(
            OtherCustomer,
            new CreateDisputeCommand
            {
                TransactionId = OtherTransaction,
                Category = "Unrecognized",
                Reason = "I did not make this transaction",
                Description = "Investigate this legacy evidence case."
            },
            "legacy-evidence-" + Guid.NewGuid(),
            ct);
        var legacy = Guid.NewGuid();
        var protectedText = new TestProtector().Protect(Convert.ToBase64String(png));
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(png)).ToLowerInvariant();
        await c.ExecuteAsync(
            """
            INSERT INTO "disputes"."evidence"("publicId",
                "disputeId",
                "uploadedById",
                "fileName",
                "contentType",
                "sizeBytes",
                "sha256",
                "protectedContent",
                "scanStatus",
                "category",
                "categoryId",
                "createdAt",
                "originalFileName",
                "originalContentType",
                "originalSizeBytes",
                "originalSha256",
                "protectedOriginal")
            VALUES(@Id,
                (
                    SELECT "resolved1"."id"
                    FROM "disputes"."disputes" AS "resolved1"
                    WHERE "resolved1"."publicId" = @Case),
                (
                    SELECT "resolved2"."id"
                    FROM "identity"."users" AS "resolved2"
                    WHERE "resolved2"."publicId" = @Actor),
                'legacy.png',
                'image/png',
                @Size,
                @Hash,
                @Content,
                'Scanned',
                'PhotoOrScreenshot',
                (SELECT "id" FROM configuration."evidenceCategories" WHERE "code" = 'PhotoOrScreenshot'),
                @Now,
                'legacy-original.png',
                'image/png',
                @Size,
                @Hash,
                @Content)
            """,
            new
            {
                Id = legacy,
                Case = other.Id,
                Actor = OtherCustomer,
                Size = png.Length,
                Hash = hash,
                Content = protectedText,
                Now = DateTimeOffset.UtcNow
            });
        Assert.Equal(png, (await _repository.DownloadEvidence(OtherCustomer, other.Id, legacy, ct)).Content);
        Assert.Equal(png, (await _repository.DownloadOriginalEvidence(OtherCustomer, other.Id, legacy, ct)).Content);
    }

    [Fact]
    public async Task Large_camera_original_roundtrips_through_private_binary_storage()
    {
        var reviewed = await ReviewableRefundCase();
        using var bitmap = new SkiaSharp.SKBitmap(4000, 3200);
        var pixels = new byte[bitmap.ByteCount];
        System.Security.Cryptography.RandomNumberGenerator.Fill(pixels);
        for (var i = 3; i < pixels.Length; i += 4)
            pixels[i] = 255;
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 100);
        var original = encoded.ToArray();
        Assert.InRange(original.Length, 5 * 1024 * 1024 + 1, (await ReadEvidenceSettings()).MaximumOriginalBytes);
        using var optimizedBitmap = bitmap.Resize(
            new SkiaSharp.SKImageInfo(2400, 1920),
            new SkiaSharp.SKSamplingOptions(SkiaSharp.SKFilterMode.Linear)) ?? throw new InvalidOperationException("Photo resize failed.");
        using var optimizedImage = SkiaSharp.SKImage.FromBitmap(optimizedBitmap);
        using var optimizedData = optimizedImage.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 88);
        var primary = optimizedData.ToArray();
        Assert.InRange(primary.Length, 1, (await ReadEvidenceSettings()).MaximumFileBytes);
        var protector = new DisputePortal.Api.Service.Security.SecretProtector(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
        using var logs = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => Microsoft.Extensions.Logging.ConsoleLoggerExtensions.AddConsole(builder));
        var evidence = new EvidenceRepository(
            _connections,
            DatabaseDialect.GetDatabaseDialect(_provider),
            protector,
            new EvidencePolicyRepository(_connections),
            DisputePortal.TestFixtures.TestRuntimeConfiguration.Read(),
            logs.CreateLogger<EvidenceRepository>());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(DisputePortal.TestFixtures.TestRuntimeConfiguration.Read().EvidencePayloadWriteTimeoutSeconds + 30));
        var id = await evidence.AddEvidence(
            Customer,
            reviewed.Id,
            "optimized.jpg",
            primary,
            "Scanned",
            "PhotoOrScreenshot",
            deadline.Token,
            new("camera.jpg", original));
        Assert.Equal(original, (await evidence.DownloadOriginalEvidence(Customer, reviewed.Id, id, deadline.Token)).Content);
        Assert.Equal(primary, (await evidence.DownloadEvidence(Customer, reviewed.Id, id, deadline.Token)).Content);
    }

    [Fact]
    public async Task Super_admin_document_access_is_audited_and_revoked_with_the_role()
    {
        var ct = CancellationToken.None;
        var reviewed = await ReviewableRefundCase();
        var id = await _repository.AddEvidence(Customer, reviewed.Id, "identity.png", EvidencePng(), "Scanned", "IdentityDocument", ct);
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
            WHERE "roles"."code" = 'super_admin'
            """,
            new { Id = OtherCustomer });
        Assert.True((await _repository.GetEvidence(OtherCustomer, reviewed.Id, ct)).Single().CanView);
        Assert.Equal(EvidencePng(), (await _repository.PreviewEvidence(OtherCustomer, reviewed.Id, id, ct)).Content);
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "audit"."auditEvents" AS "admin_audit_logs"
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "admin_audit_logs"."actorUserId") = @Actor
            AND "admin_audit_logs"."entityPublicId" = @Case
            AND "admin_audit_logs"."action" = 'EvidencePreviewRequested'
            """,
                new { Actor = OtherCustomer, Case = reviewed.Id }));
        await c.ExecuteAsync(
            """
            DELETE
            FROM "identity"."userRoles"
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "userRoles"."userId") = @Id
            AND(
                SELECT "related2"."id"
                FROM "identity"."roles" AS "related2"
                WHERE "related2"."id" = "userRoles"."roleId") IN(
                SELECT "roles"."id" AS "id"
                FROM "identity"."roles" AS "roles"
                WHERE "roles"."code" = 'super_admin')
            """,
            new { Id = OtherCustomer });
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.DownloadEvidence(OtherCustomer, reviewed.Id, id, ct));
    }

    [Fact]
    public async Task History_filters_use_owned_cases_local_calendar_dates_and_unread_state()
    {
        var ct = CancellationToken.None;
        var reviewed = await ReviewableRefundCase();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(2)).DateTime);
        var day = new DisputePortal.Api.Models.Internal.HistoryFilter
        {
            Status = "UnderReview",
            From = today,
            To = today
        };
        Assert.Single((await _repository.GetCustomerDisputes(Customer, 1, 20, ct, filter: day)).Items);
        Assert.Empty((await _repository.GetCustomerDisputes(OtherCustomer, 1, 20, ct, filter: day)).Items);
        Assert.Empty((await _repository.GetCustomerDisputes(
            Customer,
            1,
            20,
            ct,
            filter: new global::DisputePortal.Api.Models.Internal.HistoryFilter(day)
            {
                To = today.AddDays(-1),
                From = today.AddDays(-1)
            })).Items);
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.GetCustomerDisputes(
            Customer,
            1,
            20,
            ct,
            filter: new global::DisputePortal.Api.Models.Internal.HistoryFilter(day) { From = today.AddDays(1) }));
        Assert.Single((await _repository.GetAdminQueue(
            new global::DisputePortal.Api.Models.Internal.AdminQueueFilter
            {
                Page = 1,
                PageSize = 20,
                Search = null,
                Status = "active",
                Priority = null,
                From = today,
                To = today
            },
            ct,
            Agent)).Items);
        Assert.Empty((await _repository.GetAdminQueue(
            new global::DisputePortal.Api.Models.Internal.AdminQueueFilter
            {
                Page = 1,
                PageSize = 20,
                Search = null,
                Status = "active",
                Priority = null,
                From = today.AddDays(-1),
                To = today.AddDays(-1)
            },
            ct,
            Agent)).Items);
        var thread = await _repository.SendMessage(
            Customer,
            new(null, reviewed.Id, "Case question", "Please review my supporting evidence.", Transaction, Guid.NewGuid()),
            ct);
        Assert.Single(await _repository.GetThreads(
            Agent,
            ct,
            filter: new global::DisputePortal.Api.Models.Internal.HistoryFilter(day) { UnreadOnly = true }));
        Assert.Empty(await _repository.GetThreads(
            Customer,
            ct,
            filter: new global::DisputePortal.Api.Models.Internal.HistoryFilter(day) { UnreadOnly = true }));
        Assert.Empty(await _repository.GetThreads(
            Agent,
            ct,
            filter: new global::DisputePortal.Api.Models.Internal.HistoryFilter(day)
            {
                From = today.AddDays(-1),
                To = today.AddDays(-1)
            }));
        var message = Assert.Single(await _repository.GetMessages(Agent, thread, ct));
        await _repository.MarkSupportRead(Agent, thread, message.Id, ct);
        Assert.Empty(await _repository.GetThreads(
            Agent,
            ct,
            filter: new global::DisputePortal.Api.Models.Internal.HistoryFilter(day) { UnreadOnly = true }));
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.GetThreads(
            Agent,
            ct,
            filter: new global::DisputePortal.Api.Models.Internal.HistoryFilter(day) { Status = "invalid" }));
    }
}
