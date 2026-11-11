using DisputePortal.Api.Models.Domain;
using Dapper;
using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Service.Disputes;
using DisputePortal.Api.Models.Requests;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Target_changes_identify_the_actor_and_customer_activity_contains_only_public_case_progress()
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
                Reason = "Not recognised",
                Description = "Please review the merchant purchase."
            },
            "target-audit",
            ct);
        var row = await _repository.GetCase(OtherCustomer, created.Id, ct);
        var response = row.CreatedAt.AddHours(6);
        var resolution = row.CreatedAt.AddDays(3);
        await _repository.UpdateCaseDeadlines(
            OtherCustomer,
            row.Id,
            new UpdateCaseDeadlinesRequest(row.Version, response, resolution, "Approved extension while awaiting bank records."),
            ct);
        var internalActivity = await Finance().GetCaseActivity(OtherCustomer, row.Id, ct);
        var changed = Assert.Single(internalActivity, x => x.Action == "CaseTargetsChanged");
        Assert.Equal("Lindiwe Maseko", changed.Actor);
        Assert.Equal(OtherCustomer, changed.ActorIdentity!.UserId);
        Assert.Contains("super_admin", changed.ActorIdentity.Roles);
        Assert.Contains("Approved extension", changed.Detail);
        Assert.Contains("Resolution target", changed.Detail);
        var customer = await Finance().GetCaseActivity(Customer, row.Id, ct);
        Assert.DoesNotContain(
            customer,
            x => x.Action is "CaseTargetsChanged" or "CaseTriaged" or "DisputeRefundApproved" or "DisputeRefundPosted" or "AutomaticDecision");
        Assert.Equal("Kabelo Dube", Assert.Single(customer, x => x.Action == "CaseAssigned").Actor);
        Assert.All(
            customer.Where(x => x.ActorIdentity is not null),
            x =>
        {
            Assert.Empty(x.ActorIdentity!.Roles);
            Assert.Null(x.ActorIdentity.LoginIdentifier);
            Assert.Null(x.ActorIdentity.UserId);
        });
        Assert.Equal(customer.OrderByDescending(x => x.OccurredAt).Select(x => x.Id), customer.Select(x => x.Id));
    }

    [Fact]
    public async Task Access_only_changes_preserve_names_and_are_attributed_to_the_admin()
    {
        var ct = CancellationToken.None;
        await using var c = await _connections.OpenConnection(ct);
        await c.ExecuteAsync(
            """
      UPDATE "identity"."customerProfiles"
      SET "firstName" = 'Thandi',
      "lastName" = 'Ndlovu',
      "displayName" = 'Thandi Ndlovu'
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
        await _repository.UpdateUser(OtherCustomer, Agent, new UpdateUserRequest("payments_specialist", "Locked", 3), ct);
        var user = Assert.Single(await _repository.GetStaff(OtherCustomer, true, ct), x => x.Id == Agent);
        Assert.Equal("Thandi", user.FirstName);
        Assert.Equal("Ndlovu", user.LastName);
        Assert.Equal("Locked", user.Status);
        Assert.Equal("payments_specialist", user.Role);
        var audit = Assert.Single((await _repository.GetAuditLogs(1, 100, "UserAccessChanged", ct)).Items);
        Assert.Equal("Lindiwe Maseko", audit.ActorName);
        Assert.Equal(OtherCustomer, audit.ActorIdentity!.UserId);
    }

    [Fact]
    public async Task Transactions_use_saved_creation_order_and_refund_receipts_use_the_persisted_credit_reference()
    {
        var ct = CancellationToken.None;
        await using var c = await _connections.OpenConnection(ct);
        // Deliberately opposite transaction and ingestion times prove the default is saved creation time.
        var newest = Guid.NewGuid();
        await c.ExecuteAsync(
            """
      UPDATE "transactions"."transactions"
      SET "createdAt" = @Oldest,
      "transactionAt" = @Newest
      WHERE "transactions"."publicId" = @Original;
      INSERT INTO "transactions"."transactions"("publicId",
          "accountId",
          "transactionReference",
          "createdAt",
          "transactionAt",
          "description",
          "merchantName",
          "amount",
          "currencyCode",
          "transactionTypeId",
          "direction",
          "transactionStatus",
          "disputeEligible")
      VALUES(@New,
          (
              SELECT "resolved1"."id"
              FROM "accounts"."accounts" AS "resolved1"
              WHERE "resolved1"."publicId" = @Account),
          'ORDER-TEST',
          @Newest,
          @Oldest,
          'Earlier bank payment imported later',
          'Shop',
          25,
          'ZAR',
          (
              SELECT "resolved2"."id"
              FROM "configuration"."transactionTypes" AS "resolved2"
              WHERE "resolved2"."code" = 'CardPurchase'),
          'Debit',
          'Posted',
          @Eligible)
      """,
            new
            {
                Account,
                New = newest,
                Original = Transaction,
                Newest = DateTimeOffset.UtcNow.AddMinutes(-1),
                Oldest = DateTimeOffset.UtcNow.AddDays(-2),
                Eligible = true
            });
        var page = await _repository.GetTransactions(
            Customer,
            new TransactionFilter
            {
                Page = 1,
                PageSize = 20,
                Search = null,
                From = null,
                To = null,
                Direction = null,
                Type = null,
                Sort = "date_desc"
            },
            ct);
        Assert.Equal(newest, page.Items[0].Id);
        Assert.Null(page.Items[0].Reference);
        Assert.NotEmpty(page.Items[0].TypeLabel!);
        var reviewed = await ReviewableRefundCase();
        var credit = await Finance().ApproveRefund(
            Agent,
            reviewed.Id,
            new ApproveRefundRequest(reviewed.Version, Guid.NewGuid(), "Merchant confirmed refund eligibility."),
            "receipt-order",
            ct);
        var reference = await c.ExecuteScalarAsync<string>(
            """
      SELECT "transactions"."transactionReference" AS "transaction_reference"
      FROM "transactions"."transactions" AS "transactions"
      WHERE "transactions"."publicId" = @Id
      """,
            new { Id = credit.CreditTransactionId });
        Assert.Equal(reference, credit.CreditReference);
        var receipt = (await _repository.GetTransaction(Customer, credit.CreditTransactionId, ct))!;
        Assert.Equal(credit.CustomerReference, receipt.Reference);
        Assert.Matches("^RF-[0-9]{8}$", receipt.Reference!);
        Assert.Equal("Refund", receipt.Type);
        var customerReceipt = (await Finance().GetCaseFinancials(Customer, reviewed.Id, ct)).Refund!;
        Assert.Null(customerReceipt.CreditReference);
        Assert.Equal(credit.CustomerReference, customerReceipt.CustomerReference);
        var storedShort = await c.ExecuteScalarAsync<string>(
            """
      SELECT "refund_receipts"."customerReference" AS "customer_reference"
      FROM "disputes"."refundReceipts" AS "refund_receipts"
      WHERE(
          SELECT(
              SELECT "related2"."publicId"
              FROM "disputes"."disputes" AS "related2"
              WHERE "related2"."id" = "related1"."disputeId")
          FROM "disputes"."refunds" AS "related1"
          WHERE "related1"."id" = "refund_receipts"."refundId") = @Id
      """,
            new { Id = reviewed.Id });
        Assert.Equal(storedShort, credit.CustomerReference);
        var staff = new DisputePortal.Api.Repository.StaffTransactionRepository(
            _connections,
            DisputePortal.Api.Repository.Data.DatabaseDialect.GetDatabaseDialect(_provider),
            TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"),
            new TestProtector());
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
        var staffCredit = (await staff.GetStaffTransactionDetails(OtherCustomer, credit.CreditTransactionId, ct)).Transaction;
        Assert.Equal(reference, staffCredit.Reference);
        Assert.Equal(storedShort, staffCredit.RefundReference);
        Assert.Equal(reviewed.Id, staffCredit.DisputeId);
        var active = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = newest,
                Category = "Unrecognized",
                Reason = "Not recognised",
                Description = "Please investigate the other payment."
            },
            "active-order",
            ct);
        await c.ExecuteAsync(
            """
      UPDATE "disputes"."disputes"
      SET "createdAt" = @Older
      WHERE "disputes"."publicId" = @Id
      """,
            new
            {
                Id = active.Id,
                Older = DateTimeOffset.UtcNow.AddDays(-1),
                Admin = OtherCustomer
            });
        var history = await _repository.GetCustomerDisputes(Customer, 1, 20, ct);
        Assert.Equal(active.Id, history.Items[0].Id);
        Assert.Equal("Resolved", history.Items[^1].Status);
        var queue = await _repository.GetAdminQueue(
            new AdminQueueFilter
            {
                Page = 1,
                PageSize = 20,
                Search = null,
                Status = null,
                Priority = null
            },
            ct,
            OtherCustomer);
        Assert.Equal(active.Id, queue.Items[0].Dispute.Id);
        Assert.Equal("Resolved", queue.Items[^1].Dispute.Status);
    }
}
