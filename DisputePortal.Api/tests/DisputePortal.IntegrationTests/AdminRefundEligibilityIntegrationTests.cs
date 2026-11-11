using DisputePortal.TestFixtures;
using Dapper;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Repository;
using DisputePortal.Api.Repository.Data;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Theory]
    [InlineData("super_admin", false)]
    [InlineData("operations_manager", false)]
    [InlineData("super_admin", true)]
    [InlineData("operations_manager", true)]
    public async Task Admin_refund_authority_allows_unassigned_cases_without_losing_a_customer_link(string role, bool ownCustomer)
    {
        var ct = CancellationToken.None;
        var actor = ownCustomer ? Customer : OtherCustomer;
        await GrantRefundSupervisor(actor, role, ct);
        var reviewed = await ReviewableRefundCase();
        await _repository.Assign(
            actor,
            reviewed.Id,
            new AssignmentRequest(reviewed.Version, null, "A supervisor will make the financial decision."),
            ct);
        var caseState = await _repository.GetCase(actor, reviewed.Id, ct);
        Assert.Null(caseState.AssignedTo);
        Assert.Equal("UnderReview", caseState.Status);
        // Require the supervisor branch even for the fixture's small original debit.
        // This configures only this test repository; production thresholds remain unchanged.
        var finance = new FinanceRepository(
            _connections,
            DatabaseDialect.GetDatabaseDialect(_provider),
            TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"),
            TestRuntimeConfiguration.Read(),
            new CaseServicePolicy { HighValueThreshold = 50m });
        var summary = await finance.GetCaseFinancials(actor, reviewed.Id, ct);
        Assert.True(summary.CanApproveRefund);
        var before = Assert.Single(await finance.GetBalances(Customer, ct), balance => balance.AccountId == Account);
        var request = new ApproveRefundRequest(
            caseState.Version,
            Guid.NewGuid(),
            "The original posted debit has been reviewed and the refund approved.");
        var refund = await finance.ApproveRefund(actor, reviewed.Id, request, "admin-refund", ct);
        var replay = await finance.ApproveRefund(actor, reviewed.Id, request, "admin-refund-replay", ct);
        Assert.Equal(refund.CreditTransactionId, replay.CreditTransactionId);
        Assert.True(replay.Replayed);
        Assert.False(refund.Automatic);
        Assert.Equal(100m, refund.Amount);
        Assert.Equal(
            before.CurrentBalance + 100m,
            Assert.Single(await finance.GetBalances(Customer, ct), balance => balance.AccountId == Account).CurrentBalance);
        Assert.Equal("Resolved", (await _repository.GetCustomerDispute(Customer, reviewed.Id, ct))!.Dispute.Status);
        Assert.False((await finance.GetCaseFinancials(actor, reviewed.Id, ct)).CanApproveRefund);
        await using var connection = await _connections.OpenConnection(ct);
        Assert.Equal(
            1,
            await connection.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "disputes"."refunds" AS "dispute_refunds"
            WHERE(
                SELECT "related1"."publicId"
                FROM "disputes"."disputes" AS "related1"
                WHERE "related1"."id" = "dispute_refunds"."disputeId") = @Dispute
            AND(
                SELECT "related2"."publicId"
                FROM "identity"."users" AS "related2"
                WHERE "related2"."id" = "dispute_refunds"."approvedById") = @Actor
            AND "dispute_refunds"."requestId" = @Request
            """,
                new
                {
                    Dispute = reviewed.Id,
                    Actor = actor,
                    Request = request.RequestId
                }));
    }

    [Fact]
    public async Task Admin_refund_authority_still_requires_explicit_financial_permission()
    {
        var ct = CancellationToken.None;
        await GrantRefundSupervisor(OtherCustomer, "super_admin", ct);
        var reviewed = await ReviewableRefundCase();
        await using var connection = await _connections.OpenConnection(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE
            FROM "identity"."rolePermissions"
            WHERE "rolePermissions"."roleId" =(
                SELECT "roles"."id" AS "row_id"
                FROM "identity"."roles" AS "roles"
                WHERE "roles"."code" = 'super_admin')
            AND "rolePermissions"."permissionId" =(
                SELECT "permissions"."id" AS "row_id"
                FROM "identity"."permissions" AS "permissions"
                WHERE "permissions"."code" = 'refund.approve')
            """,
            cancellationToken: ct));
        var finance = Finance();
        Assert.False((await finance.GetCaseFinancials(OtherCustomer, reviewed.Id, ct)).CanApproveRefund);
        await Assert.ThrowsAsync<PortalForbiddenException>(() => finance.ApproveRefund(
            OtherCustomer,
            reviewed.Id,
            new ApproveRefundRequest(
                reviewed.Version,
                Guid.NewGuid(),
                "An administrator without financial authority cannot approve this refund."),
            "admin-permission-denied",
            ct));
        Assert.False((await finance.GetCaseFinancials(Customer, reviewed.Id, ct)).CanApproveRefund);
        await Assert.ThrowsAsync<PortalNotFoundException>(() => finance.ApproveRefund(
            Customer,
            reviewed.Id,
            new ApproveRefundRequest(reviewed.Version, Guid.NewGuid(), "A customer cannot use the staff financial approval endpoint."),
            "customer-permission-denied",
            ct));
        Assert.Equal(
            0,
            await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "disputes"."refunds" AS "dispute_refunds"
            """));
        Assert.Equal("UnderReview", (await _repository.GetCustomerDispute(Customer, reviewed.Id, ct))!.Dispute.Status);
    }

    [Fact]
    public async Task Admin_refund_authority_preserves_payment_eligibility_and_rollback()
    {
        var ct = CancellationToken.None;
        await GrantRefundSupervisor(OtherCustomer, "super_admin", ct);
        var reviewed = await ReviewableRefundCase();
        await using var connection = await _connections.OpenConnection(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE "transactions"."transactions"
            SET "transactionStatus" = 'Reversed'
            WHERE "transactions"."publicId" = @Transaction
            """,
            new { Transaction },
            cancellationToken: ct));
        var finance = Finance();
        var before = Assert.Single(await finance.GetBalances(Customer, ct), balance => balance.AccountId == Account);
        await Assert.ThrowsAsync<PortalConflictException>(() => finance.ApproveRefund(
            OtherCustomer,
            reviewed.Id,
            new ApproveRefundRequest(reviewed.Version, Guid.NewGuid(), "A supervisor cannot refund a debit that has already been reversed."),
            "admin-ineligible-payment",
            ct));
        Assert.Equivalent(
            before,
            Assert.Single(await finance.GetBalances(Customer, ct), balance => balance.AccountId == Account),
            strict: true);
        Assert.Equal(
            0,
            await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "disputes"."refunds" AS "dispute_refunds"
            """));
        Assert.Equal("UnderReview", (await _repository.GetCustomerDispute(Customer, reviewed.Id, ct))!.Dispute.Status);
    }

    private async Task GrantRefundSupervisor(Guid actor, string role, CancellationToken ct)
    {
        await using var connection = await _connections.OpenConnection(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO "identity"."userRoles"("userId",
                "roleId")
            SELECT(
                SELECT "resolved1"."id"
                FROM "identity"."users" AS "resolved1"
                WHERE "resolved1"."publicId" = @Actor),
            "roles"."id"
            FROM "identity"."roles" AS "roles"
            WHERE "roles"."code" = @Role
            """,
            new { Actor = actor, Role = role },
            cancellationToken: ct));
        Assert.Equal(
            1,
            await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
            SELECT COUNT(*)
            FROM "identity"."userRoles" AS ur
            JOIN "identity"."rolePermissions" AS rp
            ON "rp"."roleId" = "ur"."roleId"
            JOIN "identity"."permissions" AS p
            ON "p"."id" = "rp"."permissionId"
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "ur"."userId") = @Actor
            AND "p"."code" = 'refund.approve'
            """,
                new { Actor = actor },
                cancellationToken: ct)));
    }
}
