using Dapper;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Service.Disputes;
using DisputePortal.Api.Repository;
using DisputePortal.Api.Repository.Data;
using DisputePortal.Api.Models.Requests;
using Xunit;
using DisputePortal.Api.Models.Domain;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Assistance_directory_exposes_full_details_only_to_admins_and_clerks_and_audits_reads()
    {
        var ct = CancellationToken.None;
        await using var c = await _connections.OpenConnection(ct);
        var store = new StaffTransactionRepository(
            _connections,
            DatabaseDialect.GetDatabaseDialect(_provider),
            TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg"),
            new TestProtector());
        await Assert.ThrowsAsync<PortalForbiddenException>(() => store.Search(Customer, 1, 20, null, null, null, null, ct));
        await Assert.ThrowsAsync<PortalForbiddenException>(() => store.GetStaffTransactionDetails(Agent, Transaction, ct));
        await c.ExecuteAsync(
            """
      INSERT INTO "identity"."userRoles"("userId",
          "roleId")
      SELECT(
          SELECT "resolved1"."id"
          FROM "identity"."users" AS "resolved1"
          WHERE "resolved1"."publicId" = @User),
      "roles"."id"
      FROM "identity"."roles" AS "roles"
      WHERE "roles"."code" = 'audit_reader'
      """,
            new { User = OtherCustomer });
        await Assert.ThrowsAsync<PortalForbiddenException>(() => store.GetStaffTransactionDetails(OtherCustomer, Transaction, ct));
        await c.ExecuteAsync(
            """
      INSERT INTO "identity"."userRoles"("userId",
          "roleId")
      SELECT(
          SELECT "resolved1"."id"
          FROM "identity"."users" AS "resolved1"
          WHERE "resolved1"."publicId" = @User),
      "roles"."id"
      FROM "identity"."roles" AS "roles"
      WHERE "roles"."code" = 'service_clerk';
      UPDATE "identity"."customerProfiles"
      SET "email" = 'client@example.test',
      "phone" = '+27780000000',
      "protectedIdentityNumber" = 'test:9001010000088',
      "identityLastFour" = '0088',
      "dateOfBirth" = @BirthDate,
      "protectedAddress" = @Address
      WHERE(
          SELECT "related2"."publicId"
          FROM "identity"."users" AS "related2"
          WHERE "related2"."id" = "customerProfiles"."userId") = @Customer;
      UPDATE "accounts"."accounts"
      SET "protectedNumber" = 'test:1234560001'
      WHERE "accounts"."publicId" = @Account
      """,
            new
            {
                User = OtherCustomer,
                Customer,
                Account,
                BirthDate = new DateTime(1990, 1, 1),
                Address = "test:" + System.Text.Json.JsonSerializer.Serialize(new ResidentialAddress(
                "1 Test Road",
                null,
                null,
                "Cape Town",
                null,
                "8000",
                "ZA"))
            });
        var page = await store.Search(OtherCustomer, 1, 20, "example", null, null, null, ct);
        Assert.Single(page.Items);
        Assert.Equal(Transaction, page.Items[0].Id);
        var detail = await store.GetStaffTransactionDetails(OtherCustomer, Transaction, ct);
        Assert.Equal("client@example.test", detail.Customer.Email);
        Assert.Equal("+27780000000", detail.Customer.Phone);
        Assert.Equal("9001010000088", detail.Customer.IdentityNumber);
        Assert.Equal("1234560001", detail.FullAccountNumber);
        Assert.Equal("1 Test Road", detail.Customer.Address!.Line1);
        Assert.Equal(
            2,
            await c.ExecuteScalarAsync<int>(
                """
      SELECT COUNT(*)
      FROM "audit"."auditEvents" AS "admin_audit_logs"
      WHERE(
          SELECT "related1"."publicId"
          FROM "identity"."users" AS "related1"
          WHERE "related1"."id" = "admin_audit_logs"."actorUserId") = @User
      AND "admin_audit_logs"."action" IN('AssistedCustomerDetailsViewed',
          'AssistedTransactionsSearched')
      """,
                new { User = OtherCustomer }));
        await c.ExecuteAsync(
            """
      UPDATE "identity"."users"
      SET "accountStatus" = 'Disabled'
      WHERE "users"."publicId" = @User
      """,
            new { User = OtherCustomer });
        await Assert.ThrowsAsync<PortalForbiddenException>(() => store.GetStaffTransactionDetails(OtherCustomer, Transaction, ct));
    }

    [Fact]
    public async Task Clerk_intake_is_customer_linked_attributed_routed_idempotent_and_cannot_refund_or_bypass_future_transactions()
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
          WHERE "resolved1"."publicId" = @User),
      "roles"."id"
      FROM "identity"."roles" AS "roles"
      WHERE "roles"."code" = 'service_clerk'
      """,
            new { User = OtherCustomer });
        var command = new CreateDisputeCommand
        {
            TransactionId = Transaction,
            Category = "Unrecognized",
            Reason = "I did not make this transaction",
            Description = "The customer reported this unauthorised purchase.",
            StaffActorId = OtherCustomer,
            RequestSpecialistReview = true
        };
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.CreateDispute(
            Customer,
            new global::DisputePortal.Api.Models.Domain.CreateDisputeCommand(command) { StaffActorId = Agent },
            "staff-permission-check",
            ct));
        var created = await _repository.CreateDispute(Customer, command, "staff-intake-key", ct);
        Assert.Equal(created.Id, (await _repository.CreateDispute(Customer, command, "staff-intake-key", ct)).Id);
        var detail = await _repository.GetCustomerDispute(Customer, created.Id, ct);
        Assert.NotNull(detail);
        Assert.Null(await _repository.GetCustomerDispute(OtherCustomer, created.Id, ct));
        Assert.Equal("Other Customer", detail.History[0].ActorName);
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.GetCase(OtherCustomer, created.Id, ct));
        var workflow = await _repository.GetCase(Customer, created.Id, ct);
        Assert.Equal("Fraud", workflow.QueueType);
        Assert.Equal(Agent, workflow.AssignedTo);
        Assert.Equal(5, workflow.Priority);
        Assert.Equal(
            OtherCustomer,
            await c.ExecuteScalarAsync<Guid>(
                """
      SELECT(
          SELECT "related1"."publicId"
          FROM "identity"."users" AS "related1"
          WHERE "related1"."id" = "transaction_disputes"."submittedById") AS "submitted_by"
      FROM "disputes"."disputes" AS "transaction_disputes"
      WHERE "transaction_disputes"."publicId" = @Id
      """,
                new { created.Id }));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.TransitionDispute(
            OtherCustomer,
            created.Id,
            new TransitionCommand(workflow.Version, "UnderReview", "Start review."),
            "clerk-denied",
            ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.CreateDispute(
            Customer,
            new global::DisputePortal.Api.Models.Domain.CreateDisputeCommand(command) { Description = "Changed customer instructions." },
            "staff-intake-key",
            ct));
        await c.ExecuteAsync(
            """
      UPDATE "transactions"."transactions"
      SET "transactionAt" = @Future
      WHERE "transactions"."publicId" = @Id
      """,
            new { Id = OtherTransaction, Future = DateTimeOffset.UtcNow.AddDays(2) });
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.CreateDispute(
            OtherCustomer,
            new global::DisputePortal.Api.Models.Domain.CreateDisputeCommand(command) { TransactionId = OtherTransaction },
            "future-staff-intake",
            ct));
        Assert.Equal(
            0,
            await c.ExecuteScalarAsync<int>("""
      SELECT COUNT(*)
      FROM "disputes"."refunds" AS "dispute_refunds"
      """));
    }

    [Fact]
    public async Task Blocked_mandate_can_request_a_tracked_specialist_case_without_enabling_self_service_reversal()
    {
        var ct = CancellationToken.None;
        await using var c = await _connections.OpenConnection(ct);
        await c.ExecuteAsync(
            """
      UPDATE "transactions"."transactions"
      SET "transactionTypeId" =(
          SELECT "resolved1"."id"
          FROM "configuration"."transactionTypes" AS "resolved1"
          WHERE "resolved1"."code" = 'DebitOrder'),
      "debitOrderScheme" = 'DebiCheck',
      "mandateStatus" = 'Valid'
      WHERE "transactions"."publicId" = @Id
      """,
            new { Id = Transaction });
        var decision = await _repository.AssessDispute(Customer, Transaction, ct);
        Assert.False(decision.Allowed);
        Assert.True(decision.AssistedReview);
        var command = new CreateDisputeCommand
        {
            TransactionId = Transaction,
            Category = "DebitOrder",
            Reason = "I cancelled this debit order",
            Description = "Please investigate the cancelled collection."
        };
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.CreateDispute(Customer, command, "mandate-standard", ct));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.CreateDispute(
            OtherCustomer,
            new global::DisputePortal.Api.Models.Domain.CreateDisputeCommand(command) { RequestSpecialistReview = true },
            "mandate-owner-check",
            ct));
        await c.ExecuteAsync(
            """
      UPDATE "transactions"."transactions"
      SET "transactionTypeId" =(
          SELECT "resolved1"."id"
          FROM "configuration"."transactionTypes" AS "resolved1"
          WHERE "resolved1"."code" = 'DebitOrder'),
      "amount" = 1000
      WHERE "transactions"."publicId" = @Id
      """,
            new { Id = OtherTransaction });
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.CreateDispute(
            OtherCustomer,
            new global::DisputePortal.Api.Models.Domain.CreateDisputeCommand(command)
            {
                TransactionId = OtherTransaction,
                RequestSpecialistReview = true
            },
            "no-stepup-bypass",
            ct));
        var result = await _repository.CreateDispute(
            Customer,
            new global::DisputePortal.Api.Models.Domain.CreateDisputeCommand(command) { RequestSpecialistReview = true },
            "mandate-assisted",
            ct);
        var row = await _repository.GetCase(Customer, result.Id, ct);
        Assert.Equal("Submitted", row.Status);
        Assert.Equal("DebitOrder", row.QueueType);
        Assert.NotNull(row.DueAt);
        Assert.Equal(
            result.Id,
            (await _repository.CreateDispute(
                Customer,
                new global::DisputePortal.Api.Models.Domain.CreateDisputeCommand(command) { RequestSpecialistReview = true },
                "mandate-assisted",
                ct)).Id);
        Assert.Equal(
            0,
            await c.ExecuteScalarAsync<int>("""
      SELECT COUNT(*)
      FROM "disputes"."refunds" AS "dispute_refunds"
      """));
        Assert.False((await _repository.AssessDispute(Customer, Transaction, ct)).Allowed);
        Assert.NotEmpty((await _repository.GetCustomerDispute(Customer, result.Id, ct))!.Notes);
    }

    [Fact]
    public async Task Customer_and_account_rows_reject_physical_deletion()
    {
        var ct = CancellationToken.None;
        foreach (var target in new[]
        {
            ("\"accounts\".\"accounts\"", OtherAccount),
            ("\"identity\".\"users\"", OtherCustomer)
        }

        )
        {
            await using var c = await _connections.OpenConnection(ct);
            await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(() => c.ExecuteAsync(
                "DELETE FROM " + target.Item1 + " WHERE \"publicId\"=@Id",
                new { Id = target.Item2 }));
        }

        await using var read = await _connections.OpenConnection(ct);
        Assert.Equal(
            1,
            await read.ExecuteScalarAsync<int>(
                """
      SELECT COUNT(*)
      FROM "identity"."users" AS "users"
      WHERE "users"."publicId" = @Id
      """,
                new { Id = OtherCustomer }));
        Assert.Equal(
            1,
            await read.ExecuteScalarAsync<int>(
                """
      SELECT COUNT(*)
      FROM "accounts"."accounts" AS "customer_accounts"
      WHERE "customer_accounts"."publicId" = @Id
      """,
                new { Id = OtherAccount }));
    }
}
