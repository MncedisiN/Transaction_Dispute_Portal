using DisputePortal.Api.Models.Banking;
using Dapper;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Service.Disputes;
using DisputePortal.Api.Repository.Data;
using DisputePortal.Api.Configuration;
using DisputePortal.Api.Service.Security;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Requests;
using Microsoft.Data.SqlClient;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Security;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests : IAsyncLifetime, IClassFixture<IntegrationDatabaseTemplate>
{
    private readonly IntegrationDatabaseTemplate _template;

    public DisputeWorkflowRepositoryTests(IntegrationDatabaseTemplate template)
    {
        _template = template;
    }

    private PostgreSqlContainer? _postgres;
    private string? _sqlAdmin;
    private string? _pgAdmin;
    private readonly string _databaseName = "DisputePortal_Test_" + Guid.NewGuid().ToString("N");
    private DatabaseProvider _provider;
    private IDatabaseConnectionFactory _connections = null!;
    private RepositoryTestFixture _repository = null!;
    private static readonly Guid Customer = Guid.Parse("11000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherCustomer = Guid.Parse("11000000-0000-0000-0000-000000000002");
    private static readonly Guid Agent = Guid.Parse("11000000-0000-0000-0000-000000000003");
    private static readonly Guid Account = Guid.Parse("22000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherAccount = Guid.Parse("22000000-0000-0000-0000-000000000002");
    private static readonly Guid Transaction = Guid.Parse("33000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherTransaction = Guid.Parse("33000000-0000-0000-0000-000000000002");

    public async Task InitializeAsync()
    {
        _sqlAdmin = Environment.GetEnvironmentVariable("PORTAL_TEST_SQLSERVER");
        if (_template.Enabled)
        {
            _pgAdmin = string.IsNullOrWhiteSpace(_sqlAdmin) ? Environment.GetEnvironmentVariable("PORTAL_TEST_POSTGRESQL") : null;
            _provider = _template.Provider;
            _connections = await _template.Clone(_databaseName, CancellationToken.None);
        }
        else if (!string.IsNullOrWhiteSpace(_sqlAdmin))
        {
            _provider = DatabaseProvider.SqlServer;
            var settings = new SqlConnectionStringBuilder(_sqlAdmin)
            {
                InitialCatalog = _databaseName,
                CommandTimeout = 120
            };
            _connections = new DatabaseConnectionFactory(_provider, settings.ConnectionString);
            await using var admin = new SqlConnection(new SqlConnectionStringBuilder(_sqlAdmin) { InitialCatalog = "master" }.ConnectionString);
            await admin.OpenAsync();
            await Task.WhenAll(_connections.EnsureDatabaseExists(default), _connections.EnsureDatabaseExists(default));
            await admin.ExecuteAsync($"ALTER DATABASE [{_databaseName}] SET AUTO_CLOSE OFF", commandTimeout: 120);
        }
        else if (!string.IsNullOrWhiteSpace(_pgAdmin = Environment.GetEnvironmentVariable("PORTAL_TEST_POSTGRESQL")))
        {
            _provider = DatabaseProvider.PostgreSql;
            var adminSettings = new NpgsqlConnectionStringBuilder(_pgAdmin)
            {
                Database = "postgres"
            };
            await using var admin = new NpgsqlConnection(adminSettings.ConnectionString);
            await admin.OpenAsync();
            var settings = new NpgsqlConnectionStringBuilder(_pgAdmin)
            {
                Database = _databaseName
            };
            _connections = new DatabaseConnectionFactory(_provider, settings.ConnectionString);
            await Task.WhenAll(_connections.EnsureDatabaseExists(default), _connections.EnsureDatabaseExists(default));
        }
        else
        {
            _provider = DatabaseProvider.PostgreSql;
            _postgres = new PostgreSqlBuilder("postgres:16-alpine").WithDatabase("dispute_portal_test").WithUsername("portal").WithPassword("test-only-password").Build();
            await _postgres.StartAsync();
            _connections = new DatabaseConnectionFactory(_provider, _postgres.GetConnectionString());
        }

        if (!_template.Enabled)
        {
            await new DatabaseInitializer(
                _connections,
                DatabaseDialect.GetDatabaseDialect(_provider),
                DisputePortal.TestFixtures.TestRuntimeConfiguration.Read()).InitializeDatabase(CancellationToken.None);
        }

        await using var connection = await _connections.OpenConnection(CancellationToken.None);
        await connection.ExecuteAsync(
            """
            INSERT INTO "identity"."users"("publicId",
                "loginIdentifier",
                "normalizedLoginIdentifier",
                "passwordHash")
            VALUES(@Customer,
                'customer@test.local',
                'CUSTOMER@TEST.LOCAL',
                'not-used'),
            (@Other,
                'other@test.local',
                'OTHER@TEST.LOCAL',
                'not-used'),
            (@Agent,
                'agent@test.local',
                'AGENT@TEST.LOCAL',
                'not-used');
            INSERT INTO "identity"."customerProfiles"("userId",
                "displayName",
                "maskedEmail",
                "maskedPhone")
            VALUES((
                    SELECT "resolved1"."id"
                    FROM "identity"."users" AS "resolved1"
                    WHERE "resolved1"."publicId" = @Customer),
                'Test Customer',
                't•••@test.local',
                '•••••••0001'),
            ((
                    SELECT "resolved2"."id"
                    FROM "identity"."users" AS "resolved2"
                    WHERE "resolved2"."publicId" = @Other),
                'Other Customer',
                'o•••@test.local',
                '•••••••0002'),
            ((
                    SELECT "resolved3"."id"
                    FROM "identity"."users" AS "resolved3"
                    WHERE "resolved3"."publicId" = @Agent),
                'Test Agent',
                'a•••@test.local',
                '•••••••0003');
            INSERT INTO "accounts"."accounts"("publicId",
                "userId",
                "accountType",
                "currencyCode",
                "maskedAccountNumber",
                "createdAt")
            VALUES(@Account,
                (
                    SELECT "resolved4"."id"
                    FROM "identity"."users" AS "resolved4"
                    WHERE "resolved4"."publicId" = @Customer),
                'Cheque',
                'ZAR',
                '••••••••0001',
                @OpenedAt),
            (@OtherAccount,
                (
                    SELECT "resolved5"."id"
                    FROM "identity"."users" AS "resolved5"
                    WHERE "resolved5"."publicId" = @Other),
                'Cheque',
                'ZAR',
                '••••••••0002',
                @OpenedAt);
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
            VALUES(@Transaction,
                (
                    SELECT "resolved6"."id"
                    FROM "accounts"."accounts" AS "resolved6"
                    WHERE "resolved6"."publicId" = @Account),
                'TEST-TX-1',
                @Now,
                'Synthetic purchase',
                'Example Shop',
                100.00,
                'ZAR',
                (
                    SELECT "resolved7"."id"
                    FROM "configuration"."transactionTypes" AS "resolved7"
                    WHERE "resolved7"."code" = 'CardPurchase'),
                'Debit',
                'Posted',
                @Eligible),
            (@OtherTransaction,
                (
                    SELECT "resolved8"."id"
                    FROM "accounts"."accounts" AS "resolved8"
                    WHERE "resolved8"."publicId" = @OtherAccount),
                'TEST-TX-2',
                @Now,
                'Other synthetic purchase',
                'Other Shop',
                50.00,
                'ZAR',
                (
                    SELECT "resolved9"."id"
                    FROM "configuration"."transactionTypes" AS "resolved9"
                    WHERE "resolved9"."code" = 'CardPurchase'),
                'Debit',
                'Posted',
                @Eligible);
            """,
            new
            {
                Customer,
                Other = OtherCustomer,
                Agent,
                Account,
                OtherAccount,
                Transaction,
                OtherTransaction,
                Now = DateTimeOffset.UtcNow,
                OpenedAt = DateTimeOffset.UtcNow.AddDays(-5),
                Eligible = true
            },
            commandTimeout: 120);
        await connection.ExecuteAsync(
            """
            INSERT INTO "identity"."userRoles"("userId",
                "roleId")
            SELECT(
                SELECT "resolved1"."id"
                FROM "identity"."users" AS "resolved1"
                WHERE "resolved1"."publicId" = @Agent),
            "roles"."id"
            FROM "identity"."roles" AS "roles"
            WHERE "roles"."code" = 'fraud_agent';
            
            """ + """
            INSERT INTO "identity"."userRoles"("userId",
                "roleId")
            SELECT(
                SELECT "resolved1"."id"
                FROM "identity"."users" AS "resolved1"
                WHERE "resolved1"."publicId" = @Customer),
            "roles"."id"
            FROM "identity"."roles" AS "roles"
            WHERE "roles"."code" = 'customer';
            
            """ + """
            INSERT INTO "identity"."userRoles"("userId",
                "roleId")
            SELECT(
                SELECT "resolved1"."id"
                FROM "identity"."users" AS "resolved1"
                WHERE "resolved1"."publicId" = @Other),
            "roles"."id"
            FROM "identity"."roles" AS "roles"
            WHERE "roles"."code" = 'customer';
            
            """ + """
            INSERT INTO "support"."staffCapacity"("userId",
                "availability",
                "capacity")
            VALUES((
                    SELECT "resolved1"."id"
                    FROM "identity"."users" AS "resolved1"
                    WHERE "resolved1"."publicId" = @Agent),
                'Available',
                1)
            """,
            new
            {
                Agent,
                Customer,
                Other = OtherCustomer
            },
            commandTimeout: 120);
        _repository = new RepositoryTestFixture(
            _connections,
            DatabaseDialect.GetDatabaseDialect(_provider),
            new CaseServicePolicy(),
            new TestProtector(),
            new OtpHasher(new InfobipOptions { OtpHashKey = new string('t', 64) }));
    }

    [Fact]
    public async Task Recorded_first_response_target_survives_triage_and_escalation()
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
                Category = "Other",
                Reason = "Other",
                Description = "Review the merchant payment records."
            },
            "first-response-history",
            ct);
        var row = await _repository.GetCase(OtherCustomer, created.Id, ct);
        await _repository.UpdateCaseDeadlines(
            OtherCustomer,
            row.Id,
            new UpdateCaseDeadlinesRequest(
                row.Version,
                row.CreatedAt.AddDays(4),
                row.CreatedAt.AddDays(5),
                "Approved extension while awaiting merchant evidence."),
            ct);
        row = await _repository.GetCase(OtherCustomer, row.Id, ct);
        await _repository.TransitionDispute(
            OtherCustomer,
            row.Id,
            new TransitionCommand(row.Version, "UnderReview", "Supervisor review started."),
            "response-history",
            ct);
        var recorded = await _repository.GetCase(OtherCustomer, row.Id, ct);
        Assert.NotNull(recorded.FirstResponseAt);
        await _repository.Triage(
            OtherCustomer,
            row.Id,
            new TriageRequest(recorded.Version, 1, "Low", "Card", "Reassess case priority after initial review."),
            ct);
        var triaged = await _repository.GetCase(OtherCustomer, row.Id, ct);
        Assert.Equal(recorded.ResponseDueAt, triaged.ResponseDueAt);
        Assert.Equal(recorded.FirstResponseAt, triaged.FirstResponseAt);
        await _repository.Escalate(
            OtherCustomer,
            row.Id,
            new EscalationRequest(triaged.Version, "Senior review of the merchant records is required."),
            ct);
        var escalated = await _repository.GetCase(Customer, row.Id, ct);
        Assert.Equal(recorded.ResponseDueAt, escalated.ResponseDueAt);
        Assert.Equal(recorded.FirstResponseAt, escalated.FirstResponseAt);
        Assert.Equal("UnderReview", escalated.Status);
        Assert.Equal(1, escalated.EscalationLevel);
        var resolution = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()).AddDays(2);
        await _repository.UpdateCaseDeadlines(
            OtherCustomer,
            row.Id,
            new UpdateCaseDeadlinesRequest(
                escalated.Version,
                recorded.ResponseDueAt!.Value,
                resolution,
                "Confirm the revised resolution after first response."),
            ct);
        var final = await _repository.GetCase(Customer, row.Id, ct);
        Assert.Equal(recorded.ResponseDueAt, final.ResponseDueAt);
        Assert.Equal(resolution, final.DueAt);
    }

    [Fact]
    public async Task Case_targets_are_supervised_versioned_and_stay_consistent_through_the_complete_workflow()
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
                Reason = "Not recognised",
                Description = "Please investigate this transaction."
            },
            "complete-case-workflow",
            ct);
        var row = await _repository.GetCase(Customer, created.Id, ct);
        Assert.Equal("Submitted", row.Status);
        Assert.Equal("Fraud", row.QueueType);
        Assert.Equal(Agent, row.AssignedTo);
        var response = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()).AddHours(2);
        var due = response.AddDays(2);
        var request = new UpdateCaseDeadlinesRequest(row.Version, response, due, "Awaiting supporting bank records.");
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.UpdateCaseDeadlines(Agent, row.Id, request, ct));
        await _repository.UpdateCaseDeadlines(OtherCustomer, row.Id, request, ct);
        var updated = await _repository.GetCase(Customer, row.Id, ct);
        Assert.Equal(due, updated.DueAt);
        Assert.Equal(response, updated.ResponseDueAt);
        Assert.Equal(row.Version + 1, updated.Version);
        Assert.Equal(row.AssignedTo, updated.AssignedTo);
        Assert.Equal(row.Status, updated.Status);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.UpdateCaseDeadlines(OtherCustomer, row.Id, request, ct));
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.UpdateCaseDeadlines(
            OtherCustomer,
            row.Id,
            new UpdateCaseDeadlinesRequest(request)
            {
                ExpectedVersion = updated.Version,
                ResponseDueAt = due.AddHours(1)
            },
            ct));
        Assert.Equal(updated.Version, (await _repository.GetCase(Customer, row.Id, ct)).Version);
        var thread = await _repository.SendMessage(
            Customer,
            new SupportSendRequest(null, row.Id, "Payment review", "Please review this payment.", Transaction),
            ct);
        await _repository.SendMessage(
            Agent,
            new SupportSendRequest(thread, row.Id, "Payment review", "Your specialist is reviewing this payment.", Transaction),
            ct);
        foreach (var status in new[]
        {
            "UnderReview",
            "AwaitingCustomer",
            "UnderReview",
            "Resolved"
        }

        )
        {
            var current = await _repository.GetCase(Customer, row.Id, ct);
            await _repository.TransitionDispute(
                Agent,
                row.Id,
                new TransitionCommand(
                    current.Version,
                    status,
                    status == "Resolved" ? "The disputed payment has been reviewed and resolved." : "Specialist review updated."),
                "complete-workflow",
                ct);
            var customer = await _repository.GetCustomerDispute(Customer, row.Id, ct);
            var operations = await _repository.GetCase(Agent, row.Id, ct);
            var transaction = await _repository.GetTransaction(Customer, Transaction, ct);
            Assert.Equal(status, customer!.Dispute.Status);
            Assert.Equal(status, (await _repository.GetTransaction(Customer, Transaction, ct))!.DisputeStatus);
            Assert.Equal(status, operations.Status);
            Assert.Equal(customer.Dispute.Version, operations.Version);
            Assert.Equal(due, operations.DueAt);
            Assert.Equal(status != "Resolved", transaction!.HasActiveDispute);
            Assert.Equal(row.Id, transaction.DisputeId);
            Assert.Equal("Posted", transaction.Status);
            Assert.Equal(100m, transaction.Amount);
            Assert.Equal(row.Id, (await _repository.GetSupportThread(Customer, thread, ct))!.DisputeId);
        }

        var closed = await _repository.GetCase(Customer, row.Id, ct);
        Assert.NotNull(closed.FirstResponseAt);
        Assert.NotNull(closed.SlaClosedAt);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.UpdateCaseDeadlines(
            OtherCustomer,
            row.Id,
            new UpdateCaseDeadlinesRequest(request)
            {
                ExpectedVersion = closed.Version,
                DueAt = due.AddDays(1)
            },
            ct));
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "audit"."auditEvents" AS "admin_audit_logs"
            WHERE "admin_audit_logs"."entityPublicId" = @Id
            AND "admin_audit_logs"."action" = 'CaseTargetsChanged'
            """,
                new { Id = row.Id }));
        Assert.Equal(
            5,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "disputes"."statusHistory" AS "dispute_status_history"
            WHERE(
                SELECT "related1"."publicId"
                FROM "disputes"."disputes" AS "related1"
                WHERE "related1"."id" = "dispute_status_history"."disputeId") = @Id
            """,
                new { Id = row.Id }));
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
            new { Id = row.Id }) > 0);
    }

    [Fact]
    public async Task Transaction_support_is_owned_persistent_incremental_and_follows_the_created_case()
    {
        var ct = CancellationToken.None;
        var first = await _repository.SendMessage(
            Customer,
            new SupportSendRequest(null, null, "Transaction question", "Please review this payment.", Transaction),
            ct);
        var replay = await _repository.SendMessage(
            Customer,
            new SupportSendRequest(null, null, "Transaction question", "I have more information.", Transaction),
            ct);
        Assert.Equal(first, replay);
        var threads = await _repository.GetThreads(Customer, ct, transactionId: Transaction);
        var linked = Assert.Single(threads);
        Assert.Equal(Transaction, linked.TransactionId);
        Assert.Null(linked.DisputeId);
        Assert.Equal(100m, linked.Amount);
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.SendMessage(
            OtherCustomer,
            new SupportSendRequest(null, null, "Not mine", "Attempt to access another transaction.", Transaction),
            ct));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.GetMessages(OtherCustomer, first, ct));
        var messages = await _repository.GetMessages(Customer, first, ct);
        Assert.Equal(2, messages.Count);
        var delta = await _repository.GetMessages(Customer, first, ct, messages[0].Sequence);
        Assert.Equal(messages[1].Id, Assert.Single(delta).Id);
        Assert.Empty(await _repository.GetMessages(Customer, first, ct, messages[1].Sequence));
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.GetMessages(Customer, first, ct, -1));
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "I did not make this transaction",
                Description = "Please investigate this transaction."
            },
            Guid.NewGuid().ToString(),
            ct);
        var saved = await _repository.GetSupportThread(Customer, first, ct);
        Assert.Equal(created.Id, saved!.DisputeId);
        Assert.Equal(Transaction, saved.TransactionId);
        Assert.Equal(
            first,
            await _repository.SendMessage(
                Customer,
                new SupportSendRequest(null, created.Id, "Case question", "Continue the same conversation.", Transaction),
                ct));
        Assert.Equal(3, (await _repository.GetMessages(Customer, first, ct)).Count);
        var transaction = await _repository.GetTransaction(Customer, Transaction, ct);
        Assert.Null(transaction!.Reference);
        var admin = await _repository.GetAdminDispute(created.Id, ct, Agent);
        Assert.Equal("TEST-TX-1", admin!.TransactionReference);
    }

    [Fact]
    public async Task Account_details_reveal_is_owned_encrypted_and_stable()
    {
        var ct = CancellationToken.None;
        await VerifyProvisionedContact(Customer, "numbers@example.test");
        var opened = await _repository.OpenAccount(Customer, Guid.NewGuid(), "Savings", 10, ct);
        var first = await _repository.RevealAccountNumber(Customer, opened.AccountId, ct);
        Assert.Equal(10, first.AccountNumber.Length);
        Assert.All(first.AccountNumber, c => Assert.True(char.IsAsciiDigit(c)));
        Assert.Equivalent(first, await _repository.RevealAccountNumber(Customer, opened.AccountId, ct), strict: true);
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.RevealAccountNumber(OtherCustomer, opened.AccountId, ct));
        await using var c = await _connections.OpenConnection(ct);
        var stored = await c.QuerySingleAsync<string>(
            """
            SELECT "customer_accounts"."protectedNumber" AS "protected_number"
            FROM "accounts"."accounts" AS "customer_accounts"
            WHERE "customer_accounts"."publicId" = @Id
            """,
            new { Id = opened.AccountId });
        Assert.NotEqual(first.AccountNumber, stored);
        Assert.Equal("test:" + first.AccountNumber, stored);
        var metadata = await c.QueryAsync<string>(
            """
            SELECT "admin_audit_logs"."safeMetadata" AS "safe_metadata"
            FROM "audit"."auditEvents" AS "admin_audit_logs"
            WHERE "admin_audit_logs"."action" = 'AccountNumberRevealed'
            AND "admin_audit_logs"."entityPublicId" = @Id
            """,
            new { Id = opened.AccountId });
        Assert.All(metadata, value => Assert.DoesNotContain(first.AccountNumber, value));
    }

    [Fact]
    public async Task Account_deletion_requires_verification_review_and_closed_cases_and_revokes_all_access()
    {
        var ct = CancellationToken.None;
        await VerifyProvisionedContact(Customer, "closure@example.test");
        var financialAccount = await _repository.OpenAccount(Customer, Guid.NewGuid(), "Savings", 10, ct);
        var retainedNumber = await _repository.RevealAccountNumber(Customer, financialAccount.AccountId, ct);
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
            WHERE "roles"."code" = 'super_admin'
            """,
            new { User = Agent });
        var session = await _repository.CreateSession(Customer, DateTimeOffset.UtcNow.AddHours(1), ct);
        var second = await _repository.CreateSession(Customer, DateTimeOffset.UtcNow.AddHours(1), ct);
        var challenge = await _repository.ReserveDeletionChallenge(Customer, "closure@example.test", "123456", ct);
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.ConfirmDeletionRequest(
            Customer,
            new ConfirmDeletionRequest(challenge.Id, "123456", null),
            ct));
        await _repository.SetDeletionChallengeReady(Customer, challenge.Id, true, ct);
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.ConfirmDeletionRequest(
            Customer,
            new ConfirmDeletionRequest(challenge.Id, "000000", null),
            ct));
        var request = await _repository.ConfirmDeletionRequest(
            Customer,
            new ConfirmDeletionRequest(challenge.Id, "123456", "Please close my account."),
            ct);
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.ConfirmDeletionRequest(
            Customer,
            new ConfirmDeletionRequest(challenge.Id, "123456", null),
            ct));
        Assert.Empty(await _repository.GetDeletionRequests(OtherCustomer, false, ct));
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.GetDeletionRequests(OtherCustomer, true, ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.CancelDeletionRequest(OtherCustomer, request, ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.ReviewDeletionRequest(
            Agent,
            request,
            new ReviewDeletionRequest(1, "Completed", "Reviewed customer identity."),
            ct));
        await _repository.ReviewDeletionRequest(
            Agent,
            request,
            new ReviewDeletionRequest(1, "UnderReview", "Review customer linked cases."),
            ct);
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "Not recognised",
                Description = "Please investigate this payment."
            },
            Guid.NewGuid().ToString(),
            ct);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.ReviewDeletionRequest(
            Agent,
            request,
            new ReviewDeletionRequest(2, "Completed", "Reviewed customer identity."),
            ct));
        await c.ExecuteAsync(
            """
            UPDATE "disputes"."disputes"
            SET "status" = 'Resolved'
            WHERE "disputes"."publicId" = @Id
            """,
            new { Id = created.Id });
        await _repository.ReviewDeletionRequest(
            Agent,
            request,
            new ReviewDeletionRequest(2, "Completed", "Reviewed customer request and retention requirements."),
            ct);
        Assert.False(await _repository.IsSessionActive(session, DateTimeOffset.UtcNow, ct));
        Assert.False(await _repository.IsSessionActive(second, DateTimeOffset.UtcNow, ct));
        Assert.Null(await _repository.FindIdentity("closure@example.test", ct));
        Assert.Equal(
            "test:" + retainedNumber.AccountNumber,
            await c.QuerySingleAsync<string>(
                """
            SELECT "customer_accounts"."protectedNumber" AS "protected_number"
            FROM "accounts"."accounts" AS "customer_accounts"
            WHERE "customer_accounts"."publicId" = @Id
            """,
                new { Id = financialAccount.AccountId }));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.RevealAccountNumber(Customer, financialAccount.AccountId, ct));
        Assert.Equal("Completed", Assert.Single(await _repository.GetDeletionRequests(Agent, true, ct)).Status);
        Assert.NotNull(await c.QuerySingleAsync<DateTimeOffset?>(
            """
            SELECT "users"."deletedAt" AS "deleted_at"
            FROM "identity"."users" AS "users"
            WHERE "users"."publicId" = @User
            """,
            new { User = Customer }));
        Assert.Equal(
            Agent,
            await c.ExecuteScalarAsync<Guid>(
                """
            SELECT(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "customer_accounts"."deletedById") AS "deleted_by"
            FROM "accounts"."accounts" AS "customer_accounts"
            WHERE "customer_accounts"."publicId" = @Id
            """,
                new { Id = Account }));
        Assert.Equal(
            "Closed account",
            await c.QuerySingleAsync<string>(
                """
            SELECT "customer_profiles"."displayName" AS "display_name"
            FROM "identity"."customerProfiles" AS "customer_profiles"
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "customer_profiles"."userId") = @User
            """,
                new { User = Customer }));
        Assert.Equal(
            1,
            await c.QuerySingleAsync<int>(
                """
            SELECT COUNT(*)
            FROM "disputes"."disputes" AS "transaction_disputes"
            WHERE "transaction_disputes"."publicId" = @Id
            """,
                new { Id = created.Id }));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.UpdateUser(
            Agent,
            Customer,
            new UpdateUserRequest("customer", "Active", 5),
            ct));
    }

    private async Task VerifyProvisionedContact(Guid user, string contact)
    {
        var challenge = await _repository.ReserveContactChallenge(user, "email", contact, "email", "123456", CancellationToken.None);
        await _repository.SetContactChallengeDelivery(user, challenge.Id, true, CancellationToken.None);
        await _repository.CompleteContactChange(user, challenge.Id, "123456", CancellationToken.None);
    }

    [Fact]
    public async Task Provisioned_first_verified_login_provisions_once_and_generated_transactions_follow_owned_disputes()
    {
        var ct = CancellationToken.None;
        var payload = System.Text.Json.JsonSerializer.Serialize(new DisputePortal.Api.Models.Internal.RegistrationPayload(
            "Demo Customer",
            "demo.new@example.test",
            "email",
            "synthetic-hash",
            "Demo",
            "Customer"));
        var registration = await _repository.CreateIdentityChallenge(
            null,
            "Registration",
            "email",
            "demo.new@example.test",
            payload,
            "123456",
            ct);
        await _repository.MarkIdentityChallengeReady(registration.ChallengeId, ct);
        var user = await _repository.CompleteRegistration(registration.ChallengeId, "123456", ct);
        Assert.Empty(await _repository.GetAccounts(user, ct));
        var attempts = await Task.WhenAll(_repository.EnsureFirstLoginAccounts(user, ct), _repository.EnsureFirstLoginAccounts(user, ct));
        Assert.Single(attempts, created => created);
        var state = await _repository.GetProvisioningStatus(user, ct);
        Assert.True(state.ContactVerified);
        Assert.False(state.IdentityRecorded);
        Assert.Equal(2, state.Accounts.Count);
        Assert.All(
            state.Accounts,
            account =>
        {
            Assert.Equal(31, account.TransactionCount);
            Assert.Equal("Completed", account.LastBatch!.State);
        });
        Assert.All(
            await Task.WhenAll(_repository.ProcessNextTransactionBatch(ct), _repository.ProcessNextTransactionBatch(ct)),
            processed => Assert.False(processed));
        Assert.False(await _repository.ProcessNextTransactionBatch(ct));
        var after = await _repository.GetProvisioningStatus(user, ct);
        Assert.All(
            after.Accounts,
            account =>
        {
            Assert.Equal(31, account.TransactionCount);
            Assert.Equal("Completed", account.LastBatch!.State);
        });
        var accountId = after.Accounts[0].AccountId;
        var transactions = await _repository.GetTransactions(
            user,
            new DisputePortal.Api.Models.Internal.TransactionFilter
            {
                Page = 1,
                PageSize = 50,
                Search = null,
                From = null,
                To = null,
                Direction = null,
                Type = null,
                Sort = "date_desc",
                AccountId = accountId
            },
            ct);
        var transaction = transactions.Items.First(item => item.DisputeEligible && item.Type == "CardPurchase");
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.CreateDispute(
            OtherCustomer,
            new CreateDisputeCommand
            {
                TransactionId = transaction.Id,
                Category = "Unrecognized",
                Reason = "Not recognised",
                Description = "Review this synthetic transaction."
            },
            "foreign-demo-01",
            ct));
        var session = await _repository.CreateSession(user, DateTimeOffset.UtcNow.AddMinutes(20), ct);
        var stepUp = await _repository.ReserveDisputeVerification(
            user,
            session,
            transaction.Id,
            "email",
            "demo.new@example.test",
            "234567",
            ct);
        await _repository.ReadyDisputeVerification(user, stepUp.ChallengeId, ct);
        await _repository.CompleteDisputeVerification(user, session, transaction.Id, stepUp.ChallengeId, "234567", ct);
        var command = new global::DisputePortal.Api.Models.Domain.CreateDisputeCommand(new CreateDisputeCommand
        {
            TransactionId = transaction.Id,
            Category = "Unrecognized",
            Reason = "Not recognised",
            Description = "Review this synthetic transaction."
        })
        {
            SessionId = session,
            VerificationId = stepUp.ChallengeId
        };
        var dispute = await _repository.CreateDispute(user, command, "own-demo-01", ct);
        Assert.Contains((await _repository.GetCustomerDisputes(user, 1, 20, ct, accountId)).Items, item => item.Id == dispute.Id);
        Assert.DoesNotContain(await _repository.GetAccounts(OtherCustomer, ct), account => account.Id == accountId);
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.QueueTransactions(
            OtherCustomer,
            accountId,
            Guid.NewGuid(),
            10,
            ct));
        Assert.False(await _repository.EnsureFirstLoginAccounts(user, ct));
        Assert.Equal(2, (await _repository.GetProvisioningStatus(user, ct)).Accounts.Count);
    }

    [Fact]
    public async Task Provisioned_account_and_outbox_requests_are_idempotent_and_ownership_checked()
    {
        var ct = CancellationToken.None;
        await VerifyProvisionedContact(Customer, "demo.owner@example.test");
        var request = Guid.NewGuid();
        var account = await _repository.OpenAccount(Customer, request, "Savings", 10, ct);
        Assert.Equivalent(account, await _repository.OpenAccount(Customer, request, "Savings", 10, ct), strict: true);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.OpenAccount(Customer, request, "Cheque", 10, ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.QueueTransactions(
            Customer,
            account.AccountId,
            Guid.NewGuid(),
            10,
            ct));
        Assert.True(await _repository.ProcessNextTransactionBatch(ct));
        var next = Guid.NewGuid();
        var queued = await _repository.QueueTransactions(Customer, account.AccountId, next, 7, ct);
        Assert.Equivalent(queued, await _repository.QueueTransactions(Customer, account.AccountId, next, 7, ct), strict: true);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.QueueTransactions(Customer, account.AccountId, next, 8, ct));
        Assert.True(await _repository.ProcessNextTransactionBatch(ct));
        Assert.False(await _repository.ProcessNextTransactionBatch(ct));
        Assert.Equal(
            18,
            Assert.Single((await _repository.GetProvisioningStatus(Customer, ct)).Accounts, item => item.AccountId == account.AccountId).TransactionCount);
        Assert.Equal(2, (await _repository.GetProvisioningStatus(Customer, ct)).AccountLimit);
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.OpenAccount(Customer, Guid.NewGuid(), "Credit", 1, ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.OpenAccount(Customer, Guid.NewGuid(), "Savings", 1, ct));
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.QueueTransactions(
            Customer,
            account.AccountId,
            Guid.NewGuid(),
            51,
            ct));
    }

    [Fact]
    public async Task Provisioned_profile_links_a_later_locked_id_without_exposing_or_reassigning_it()
    {
        var ct = CancellationToken.None;
        await VerifyProvisionedContact(Customer, "demo.identity@example.test");
        var account = await _repository.OpenAccount(Customer, Guid.NewGuid(), "Savings", 1, ct);
        await _repository.SetIdentity(Customer, "9001155000085", 1900, ct);
        Assert.True((await _repository.GetProvisioningStatus(Customer, ct)).IdentityRecorded);
        await using var c = await _connections.OpenConnection(ct);
        var hash = await c.ExecuteScalarAsync<string>(
            """
            SELECT "demo_bank_identities"."identityHash" AS "identity_hash"
            FROM "integration"."localBankingIdentities" AS "demo_bank_identities"
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "demo_bank_identities"."userId") = @User
            """,
            new { User = Customer });
        Assert.Equal(64, hash!.Length);
        Assert.DoesNotContain("9001155000085", hash);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.SetIdentity(OtherCustomer, "9001155000085", 1900, ct));
        Assert.Equal(
            account.AccountId,
            Assert.Single((await _repository.GetProvisioningStatus(Customer, ct)).Accounts, item => item.AccountId == account.AccountId).AccountId);
    }

    [Fact]
    public async Task Provisioned_jobs_do_not_generate_activity_for_inactive_accounts()
    {
        var ct = CancellationToken.None;
        await VerifyProvisionedContact(Customer, "demo.inactive@example.test");
        var account = await _repository.OpenAccount(Customer, Guid.NewGuid(), "Savings", 10, ct);
        await using var c = await _connections.OpenConnection(ct);
        await c.ExecuteAsync(
            """
            UPDATE "accounts"."accounts"
            SET "accountStatus" = 'Blocked'
            WHERE "accounts"."publicId" = @Id
            """,
            new { Id = account.AccountId });
        Assert.True(await _repository.ProcessNextTransactionBatch(ct));
        var result = Assert.Single(
            (await _repository.GetProvisioningStatus(Customer, ct)).Accounts,
            item => item.AccountId == account.AccountId);
        Assert.Equal(0, result.TransactionCount);
        Assert.Equal("Failed", result.LastBatch!.State);
    }

    [Fact]
    public async Task Provisioned_provisioning_requires_a_verified_customer_contact()
    {
        var ct = CancellationToken.None;
        Assert.False(await _repository.EnsureFirstLoginAccounts(Customer, ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.OpenAccount(Customer, Guid.NewGuid(), "Savings", 10, ct));
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.GetProvisioningStatus(Agent, ct));
        Assert.Equal(Account, Assert.Single((await _repository.GetProvisioningStatus(Customer, ct)).Accounts).AccountId);
    }

    [Fact]
    public async Task Super_admin_can_create_users_change_roles_and_revoke_previous_sessions()
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
        var request = new SaveUserRequest(
            "new.staff@test.local",
            "New Specialist",
            "dispute_agent",
            "TestPassword!2026",
            FirstName: "Test",
            LastName: "Specialist");
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.CreateUser(Agent, request, "not-used", ct));
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.GetStaff(Agent, true, ct));
        var id = await _repository.CreateUser(OtherCustomer, request, "not-used", ct);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.CreateUser(
            OtherCustomer,
            new SaveUserRequest(request) { Email = "NEW.STAFF@test.local" },
            "not-used",
            ct));
        var session = await _repository.CreateSession(id, DateTimeOffset.UtcNow.AddMinutes(20), ct);
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.UpdateUser(
            Agent,
            id,
            new UpdateUserRequest("super_admin", "Active", 5),
            ct));
        await _repository.UpdateUser(OtherCustomer, id, new UpdateUserRequest("fraud_agent", "Active", 3), ct);
        var saved = Assert.Single(await _repository.GetStaff(OtherCustomer, true, ct), user => user.Id == id);
        Assert.Equal("fraud_agent", saved.Role);
        Assert.Equal(3, saved.Capacity);
        Assert.False(await _repository.IsSessionActive(session, DateTimeOffset.UtcNow, ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.UpdateUser(
            OtherCustomer,
            OtherCustomer,
            new UpdateUserRequest("customer", "Disabled", 1),
            ct));
        Assert.Equal(
            2,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "audit"."auditEvents" AS "admin_audit_logs"
            WHERE "admin_audit_logs"."entityPublicId" = @Id
            AND "admin_audit_logs"."action" IN('UserCreated',
                'UserAccessChanged')
            """,
                new { Id = id }));
    }

    [Fact]
    public async Task Manual_assignment_requires_qualified_available_staff_capacity_and_current_version()
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
        await _repository.SetAvailability(Agent, "Offline", ct);
        var first = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "Not recognised",
                Description = "Please review this transaction."
            },
            "manual-first",
            ct);
        var second = await _repository.CreateDispute(
            OtherCustomer,
            new CreateDisputeCommand
            {
                TransactionId = OtherTransaction,
                Category = "Unrecognized",
                Reason = "Not recognised",
                Description = "Please review this transaction."
            },
            "manual-second",
            ct);
        var fraud = await _repository.CreateUser(
            OtherCustomer,
            new SaveUserRequest(
                "fraud.new@test.local",
                "Fraud Specialist",
                "fraud_agent",
                "TestPassword!2026",
                FirstName: "Test",
                LastName: "Specialist"),
            "not-used",
            ct);
        var card = await _repository.CreateUser(
            OtherCustomer,
            new SaveUserRequest(
                "card.new@test.local",
                "Card Specialist",
                "card_specialist",
                "TestPassword!2026",
                FirstName: "Test",
                LastName: "Specialist"),
            "not-used",
            ct);
        await _repository.UpdateUser(OtherCustomer, fraud, new UpdateUserRequest("fraud_agent", "Active", 1), ct);
        // Fixture availability is set directly so automatic dispatch cannot consume these manual-assignment cases.
        await c.ExecuteAsync(
            """
            UPDATE "support"."staffCapacity"
            SET "availability" = 'Available'
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "staffCapacity"."userId") IN(@Fraud,
                @Card)
            """,
            new { Fraud = fraud, Card = card });
        var original = await _repository.GetCase(OtherCustomer, first.Id, ct);
        Assert.Null(original.AssignedTo);
        var valid = new AssignmentRequest(original.Version, fraud, "Assign to the qualified investigator.");
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.Assign(Agent, first.Id, valid, ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.Assign(
            OtherCustomer,
            first.Id,
            new AssignmentRequest(valid) { UserId = card },
            ct));
        await c.ExecuteAsync(
            """
            UPDATE "support"."staffCapacity"
            SET "availability" = 'Busy'
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "staffCapacity"."userId") = @Id
            """,
            new { Id = fraud });
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.Assign(OtherCustomer, first.Id, valid, ct));
        await c.ExecuteAsync(
            """
            UPDATE "support"."staffCapacity"
            SET "availability" = 'Available'
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "staffCapacity"."userId") = @Id
            """,
            new { Id = fraud });
        await _repository.UpdateUser(OtherCustomer, fraud, new UpdateUserRequest("fraud_agent", "Disabled", 1), ct);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.Assign(OtherCustomer, first.Id, valid, ct));
        await _repository.UpdateUser(OtherCustomer, fraud, new UpdateUserRequest("fraud_agent", "Active", 1), ct);
        await _repository.Assign(OtherCustomer, first.Id, valid, ct);
        Assert.Equal(fraud, (await _repository.GetCase(OtherCustomer, first.Id, ct)).AssignedTo);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.Assign(
            OtherCustomer,
            first.Id,
            new AssignmentRequest(valid) { UserId = null },
            ct));
        var pending = await _repository.GetCase(OtherCustomer, second.Id, ct);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.Assign(
            OtherCustomer,
            second.Id,
            new AssignmentRequest(valid) { ExpectedVersion = pending.Version },
            ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.UpdateUser(
            OtherCustomer,
            fraud,
            new UpdateUserRequest("card_specialist", "Active", 1),
            ct));
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "audit"."auditEvents" AS "admin_audit_logs"
            WHERE "admin_audit_logs"."entityPublicId" = @Id
            AND "admin_audit_logs"."action" = 'CaseAssigned'
            """,
                new { Id = first.Id }));
    }

    [Fact]
    public async Task Escalated_cases_allow_qualified_team_handover_and_available_senior_reviewers()
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
        var senior = await _repository.CreateUser(
            OtherCustomer,
            new SaveUserRequest(
                "senior@test.local",
                "Senior Reviewer",
                "operations_manager",
                "TestPassword!2026",
                FirstName: "Test",
                LastName: "Specialist"),
            "not-used",
            ct);
        var fraud = await _repository.CreateUser(
            OtherCustomer,
            new SaveUserRequest(
                "fraud.second@test.local",
                "Second Fraud Specialist",
                "fraud_agent",
                "TestPassword!2026",
                FirstName: "Test",
                LastName: "Specialist"),
            "not-used",
            ct);
        // Additional roles must not make assignment depend on database row order.
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
            WHERE "roles"."code" = 'customer'
            """,
            new { Id = fraud });
        await _repository.SetAvailability(senior, "Available", ct);
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "Not recognised",
                Description = "Please investigate this transaction."
            },
            "senior-handover",
            ct);
        var row = await _repository.GetCase(OtherCustomer, created.Id, ct);
        Assert.Equal(Agent, row.AssignedTo);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.Assign(
            OtherCustomer,
            created.Id,
            new AssignmentRequest(row.Version, senior, "Senior case review requested."),
            ct));
        await _repository.Triage(
            OtherCustomer,
            created.Id,
            new TriageRequest(row.Version, 4, "High", "Fraud", "Review the current risk level."),
            ct);
        row = await _repository.GetCase(OtherCustomer, created.Id, ct);
        Assert.Equal(4, row.Priority);
        var beforeEscalation = row.Version;
        await _repository.Escalate(Agent, created.Id, new EscalationRequest(row.Version, "Escalate for an independent senior review."), ct);
        row = await _repository.GetCase(OtherCustomer, created.Id, ct);
        Assert.Equal(5, row.Priority);
        Assert.Equal(1, row.EscalationLevel);
        Assert.Equal(Agent, row.AssignedTo); // Preserve accountability until a handover is confirmed.
        Assert.True(row.DueAt <= row.CreatedAt.AddHours(24));
        // Escalation removes the specialist's work scope; a supervisor still reaches version validation.
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.Escalate(
            Agent,
            created.Id,
            new EscalationRequest(beforeEscalation, "Stale request must not duplicate notifications."),
            ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.Escalate(
            OtherCustomer,
            created.Id,
            new EscalationRequest(beforeEscalation, "Stale request must not duplicate notifications."),
            ct));
        Assert.Contains(await _repository.GetStaff(OtherCustomer, false, ct), person => person.Id == senior);
        await _repository.SetAvailability(fraud, "Available", ct);
        await _repository.Assign(
            OtherCustomer,
            created.Id,
            new AssignmentRequest(row.Version, fraud, "Hand over to another qualified fraud investigator."),
            ct);
        row = await _repository.GetCase(OtherCustomer, created.Id, ct);
        Assert.Equal(fraud, row.AssignedTo);
        await _repository.SetAvailability(senior, "Busy", ct);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.Assign(
            OtherCustomer,
            created.Id,
            new AssignmentRequest(row.Version, senior, "Senior reviewer takes responsibility."),
            ct));
        await _repository.SetAvailability(senior, "Available", ct);
        await _repository.Assign(
            OtherCustomer,
            created.Id,
            new AssignmentRequest(row.Version, senior, "Senior reviewer takes responsibility."),
            ct);
        row = await _repository.GetCase(OtherCustomer, created.Id, ct);
        Assert.Equal(senior, row.AssignedTo);
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "audit"."auditEvents" AS "admin_audit_logs"
            WHERE "admin_audit_logs"."entityPublicId" = @Id
            AND "admin_audit_logs"."action" = 'CaseEscalated'
            """,
                new { Id = created.Id }));
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "notifications"."notifications" AS "notifications"
            WHERE(
                SELECT "related1"."publicId"
                FROM "disputes"."disputes" AS "related1"
                WHERE "related1"."id" = "notifications"."disputeId") = @Id
            AND(
                SELECT "related2"."publicId"
                FROM "identity"."users" AS "related2"
                WHERE "related2"."id" = "notifications"."userId") = @Customer
            AND "notifications"."notificationType" = 'CaseEscalated'
            """,
                new { Id = created.Id, Customer }));
    }

    [Fact]
    public async Task Workflow_validation_and_stale_versions_never_partially_change_cases()
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
                Reason = "Not recognised",
                Description = "Please review this transaction."
            },
            "workflow-validation",
            ct);
        var row = await _repository.GetCase(OtherCustomer, created.Id, ct);
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.Triage(
            OtherCustomer,
            created.Id,
            new TriageRequest(row.Version, 6, "High", "Fraud", "Review priority."),
            ct));
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.Escalate(
            Agent,
            created.Id,
            new EscalationRequest(row.Version, "short"),
            ct));
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.Assign(
            OtherCustomer,
            created.Id,
            new AssignmentRequest(row.Version, null, " "),
            ct));
        Assert.Equal(row.Version, (await _repository.GetCase(OtherCustomer, created.Id, ct)).Version);
        await _repository.Triage(
            OtherCustomer,
            created.Id,
            new TriageRequest(row.Version, 4, "High", "Card", "Correct the specialist team."),
            ct);
        var changed = await _repository.GetCase(OtherCustomer, created.Id, ct);
        Assert.Equal("Card", changed.QueueType);
        Assert.Null(changed.AssignedTo);
        Assert.Null(changed.AssignedAt);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.Triage(
            OtherCustomer,
            created.Id,
            new TriageRequest(row.Version, 5, "Critical", "Fraud", "Reject stale risk update."),
            ct));
        Assert.Equal(changed.Version, (await _repository.GetCase(OtherCustomer, created.Id, ct)).Version);
    }

    [Fact]
    public async Task Idle_and_absolute_expiry_cannot_be_revived_by_requests()
    {
        var ct = CancellationToken.None;
        var now = DateTimeOffset.UtcNow;
        var idle = await _repository.CreateSession(Customer, now.AddHours(1), ct);
        await using var c = await _connections.OpenConnection(ct);
        await c.ExecuteAsync(
            """
            UPDATE "identity"."userSessions"
            SET "lastActivityAt" = @Old
            WHERE "userSessions"."publicId" = @Id
            """,
            new { Id = idle, Old = now.AddMinutes(-16) });
        Assert.False(await _repository.IsSessionActive(idle, now, ct));
        Assert.DoesNotContain(await _repository.GetActiveSessions(Customer, ct), s => s.Id == idle);
        Assert.False(await _repository.IsSessionActive(idle, now.AddSeconds(1), ct));
        var absolute = await _repository.CreateSession(Customer, now.AddHours(1), ct);
        await c.ExecuteAsync(
            """
            UPDATE "identity"."userSessions"
            SET "createdAt" = @Old
            WHERE "userSessions"."publicId" = @Id
            """,
            new { Id = absolute, Old = now.AddMinutes(-31) });
        Assert.False(await _repository.IsSessionActive(absolute, now, ct));
        var active = await _repository.CreateSession(Customer, now.AddMinutes(20), ct);
        Assert.True(await _repository.IsSessionActive(active, now.AddSeconds(1), ct));
        await _repository.RevokeSession(active, now.AddSeconds(2), ct);
        Assert.False(await _repository.IsSessionActive(active, now.AddSeconds(3), ct));
    }

    [Fact]
    public async Task Api_scanner_accepts_valid_images_and_rejects_unsupported_bytes()
    {
        var ct = CancellationToken.None;
        var scanner = new DisputePortal.Api.Service.Security.ApiEvidenceScanner();
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "Not recognised",
                Description = "Synthetic scanner verification."
            },
            "scanner-check",
            ct);
        var controller = new DisputePortal.Api.Controllers.EvidenceController(_repository, scanner)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, Customer.ToString())],
            "test"))
                }
            }
        };
        // This direct controller call bypasses MVC resource filters; supply their database snapshot.
        controller.HttpContext.Items[typeof(DisputePortal.Api.Models.Configuration.EvidencePolicy)] = await ReadEvidenceSettings();
        var png = EvidencePng();
        using var stream = new MemoryStream(png);
        await controller.Upload(
            created.Id,
            new Microsoft.AspNetCore.Http.FormFile(stream, 0, png.Length, "file", "clean-screenshot.png"),
            "PhotoOrScreenshot",
            ct);
        var attached = Assert.Single(await _repository.GetEvidence(Customer, created.Id, ct));
        Assert.Equal("Validated", attached.ScanStatus);
        var download = Assert.IsType<Microsoft.AspNetCore.Mvc.FileContentResult>(await controller.Download(created.Id, attached.Id, ct));
        Assert.Equal(png, download.FileContents);
        var invalid = "unsupported evidence bytes"u8.ToArray();
        using var invalidStream = new MemoryStream(invalid);
        await Assert.ThrowsAsync<PortalValidationException>(() => controller.Upload(
            created.Id,
            new Microsoft.AspNetCore.Http.FormFile(invalidStream, 0, invalid.Length, "file", "invalid.pdf"),
            "Other",
            ct));
        Assert.Single(await _repository.GetEvidence(Customer, created.Id, ct));
    }

    private static byte[] EvidencePng()
    {
        using var bitmap = new SkiaSharp.SKBitmap(32, 24);
        bitmap.Erase(SkiaSharp.SKColors.Blue);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    [Fact]
    public async Task Taking_a_case_enables_replies_and_respects_team_and_capacity()
    {
        var ct = CancellationToken.None;
        await _repository.SetAvailability(Agent, "Busy", ct);
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "Not recognised",
                Description = "Please investigate this purchase."
            },
            "claim-case",
            ct);
        var before = await _repository.GetCase(Agent, created.Id, ct);
        Assert.Null(before.AssignedTo);
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.ClaimCase(
            Customer,
            created.Id,
            new ClaimCaseRequest(before.Version, "I will handle this case."),
            ct));
        await _repository.ClaimCase(Agent, created.Id, new ClaimCaseRequest(before.Version, "I will handle this case."), ct);
        var owned = await _repository.GetCase(Agent, created.Id, ct);
        Assert.Equal(Agent, owned.AssignedTo);
        await _repository.ClaimCase(Agent, created.Id, new ClaimCaseRequest(before.Version, "Retry the same claim."), ct);
        Assert.Equal(owned.Version, (await _repository.GetCase(Agent, created.Id, ct)).Version);
        await _repository.SendMessage(Agent, new SupportSendRequest(null, created.Id, "Case update", "I am reviewing your dispute."), ct);
        await _repository.TransitionDispute(
            Agent,
            created.Id,
            new TransitionCommand(owned.Version, "UnderReview", "Review started."),
            "claim-review",
            ct);
        var another = await _repository.CreateDispute(
            OtherCustomer,
            new CreateDisputeCommand
            {
                TransactionId = OtherTransaction,
                Category = "Unrecognized",
                Reason = "Not recognised",
                Description = "Please investigate this purchase."
            },
            "claim-capacity",
            ct);
        var otherCase = await _repository.GetCase(Agent, another.Id, ct);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.ClaimCase(
            Agent,
            another.Id,
            new ClaimCaseRequest(otherCase.Version, "I want this case too."),
            ct));
    }

    [Fact]
    public async Task Competing_claims_have_one_owner_and_do_not_steal_assignments()
    {
        var ct = CancellationToken.None;
        var second = Guid.NewGuid();
        await _repository.SetAvailability(Agent, "Busy", ct);
        await using var c = await _connections.OpenConnection(ct);
        await c.ExecuteAsync(
            """
            INSERT INTO "identity"."users"("publicId",
                "loginIdentifier",
                "normalizedLoginIdentifier",
                "passwordHash")
            VALUES(@Id,
                'second@test.local',
                'SECOND@TEST.LOCAL',
                'not-used');
            INSERT INTO "identity"."customerProfiles"("userId",
                "displayName",
                "maskedEmail",
                "maskedPhone")
            VALUES((
                    SELECT "resolved1"."id"
                    FROM "identity"."users" AS "resolved1"
                    WHERE "resolved1"."publicId" = @Id),
                'Second Agent',
                's***@test.local',
                'Not supplied');
            INSERT INTO "identity"."userRoles"("userId",
                "roleId")
            SELECT(
                SELECT "resolved2"."id"
                FROM "identity"."users" AS "resolved2"
                WHERE "resolved2"."publicId" = @Id),
            "roles"."id"
            FROM "identity"."roles" AS "roles"
            WHERE "roles"."code" = 'fraud_agent';
            INSERT INTO "support"."staffCapacity"("userId",
                "availability",
                "capacity")
            VALUES((
                    SELECT "resolved3"."id"
                    FROM "identity"."users" AS "resolved3"
                    WHERE "resolved3"."publicId" = @Id),
                'Busy',
                2)
            """,
            new { Id = second });
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "Not recognised",
                Description = "Please investigate this purchase."
            },
            "claim-race",
            ct);
        var initial = await _repository.GetCase(Agent, created.Id, ct);
        async Task<bool> TryClaim(Guid actor)
        {
            try
            {
                await _repository.ClaimCase(actor, created.Id, new ClaimCaseRequest(initial.Version, "Taking ownership for review."), ct);
                return true;
            }
            catch (Exception error) when (error is PortalConflictException or PortalNotFoundException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(TryClaim(Agent), TryClaim(second));
        Assert.Single(results, x => x);
        var current = await _repository.GetCase(Customer, created.Id, ct);
        var loser = current.AssignedTo == Agent ? second : Agent;
        // Assignment revokes the losing specialist's read scope; do not expose
        // another owner's case just to return its concurrency/version details.
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.ClaimCase(
            loser,
            created.Id,
            new ClaimCaseRequest(current.Version, "Taking a colleague's case."),
            ct));
        await c.ExecuteAsync(
            """
            UPDATE "disputes"."disputes"
            SET "status" = 'Withdrawn'
            WHERE "disputes"."publicId" = @Id
            """,
            new { Id = created.Id });
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.ClaimCase(
            current.AssignedTo!.Value,
            created.Id,
            new ClaimCaseRequest(current.Version, "Taking the closed case."),
            ct));
    }

    [Fact]
    public async Task Evidence_grants_are_individual_expiring_revocable_and_require_current_eligibility()
    {
        var ct = CancellationToken.None;
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "Not recognised",
                Description = "Please investigate this purchase."
            },
            "grant-case",
            ct);
        var id = await _repository.AddEvidence(Customer, created.Id, "identity.png", EvidencePng(), "Scanned", "IdentityDocument", ct);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.AddEvidence(
            Customer,
            created.Id,
            "same.png",
            EvidencePng(),
            "Scanned",
            "ProofOfAddress",
            ct));
        var second = await _repository.AddEvidence(
            Customer,
            created.Id,
            "letter.pdf",
            System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\nPaid up letter\n%%EOF"),
            "Scanned",
            "PaidUpLetter",
            ct);
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
            new { Id = OtherCustomer });
        var manager = OtherCustomer;
        Assert.False((await _repository.GetEvidence(manager, created.Id, ct))[0].CanView);
        Assert.All(await _repository.GetEvidence(manager, created.Id, ct), file => Assert.True(file.CanManageAccess));
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.PreviewEvidence(manager, created.Id, id, ct));
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.DownloadEvidence(Agent, created.Id, id, ct));
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.GrantEvidenceAccess(
            manager,
            created.Id,
            id,
            new(manager, 24, "Review identity"),
            ct));
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.GrantEvidenceAccess(
            Customer,
            created.Id,
            id,
            new(Agent, 24, "Review identity"),
            ct));
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.GrantEvidenceAccess(
            manager,
            created.Id,
            id,
            new(Agent, 169, "Review identity"),
            ct));
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.GrantEvidenceAccess(
            manager,
            created.Id,
            id,
            new(Agent, 24, ""),
            ct));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.GrantEvidenceAccess(
            manager,
            Guid.NewGuid(),
            id,
            new(Agent, 24, "Review identity"),
            ct));
        await _repository.GrantEvidenceAccess(manager, created.Id, id, new(Agent, 24, "Review identity for this case"), ct);
        var granted = await _repository.GetEvidence(Agent, created.Id, ct);
        Assert.True(granted.Single(file => file.Id == id).CanView);
        Assert.Equal("IdentityDocument", granted.Single(file => file.Id == id).Category);
        Assert.False(granted.Single(file => file.Id == second).CanView);
        Assert.Equal(EvidencePng(), (await _repository.PreviewEvidence(Agent, created.Id, id, ct)).Content);
        Assert.Equal(EvidencePng(), (await _repository.DownloadEvidence(Agent, created.Id, id, ct)).Content);
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.PreviewEvidence(Agent, created.Id, second, ct));
        var access = await _repository.GetEvidenceAccess(manager, created.Id, id, ct);
        Assert.DoesNotContain(access.Recipients, x => x.Id == manager);
        var grant = Assert.Single(access.Grants);
        Assert.StartsWith(
            "test:",
            await c.ExecuteScalarAsync<string>(
                """
            SELECT "evidence_access_grants"."reasonProtected" AS "reason_protected"
            FROM "disputes"."evidenceAccessGrants" AS "evidence_access_grants"
            WHERE "evidence_access_grants"."publicId" = @Id
            """,
                new { grant.Id }));
        await _repository.RevokeEvidenceAccess(manager, created.Id, id, grant.Id, new("Review completed"), ct);
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.DownloadEvidence(Agent, created.Id, id, ct));
        await _repository.GrantEvidenceAccess(manager, created.Id, id, new(Agent, 1, "Review identity again"), ct);
        await c.ExecuteAsync(
            """
            UPDATE "disputes"."evidenceAccessGrants"
            SET "createdAt" = @Created,
            "expiresAt" = @Expired
            WHERE(
                SELECT "related1"."publicId"
                FROM "disputes"."evidence" AS "related1"
                WHERE "related1"."id" = "evidenceAccessGrants"."evidenceId") = @Id
            AND "evidenceAccessGrants"."revokedAt" IS NULL
            """,
            new
            {
                Id = id,
                Created = DateTimeOffset.UtcNow.AddHours(-2),
                Expired = DateTimeOffset.UtcNow.AddHours(-1)
            });
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.PreviewEvidence(Agent, created.Id, id, ct));
        await _repository.GrantEvidenceAccess(manager, created.Id, id, new(Agent, 24, "Check identity again"), ct);
        Assert.Equal(EvidencePng(), (await _repository.PreviewEvidence(Agent, created.Id, id, ct)).Content);
        await c.ExecuteAsync(
            """
            UPDATE "disputes"."disputes"
            SET "assignedToId" = NULL
            WHERE "disputes"."publicId" = @Id
            """,
            new { Id = created.Id });
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.DownloadEvidence(Agent, created.Id, id, ct));
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.GrantEvidenceAccess(
            manager,
            created.Id,
            id,
            new(Agent, 24, "Unassigned agent review"),
            ct));
        await c.ExecuteAsync(
            """
            UPDATE "disputes"."disputes"
            SET "assignedToId" =(
                SELECT "resolved1"."id"
                FROM "identity"."users" AS "resolved1"
                WHERE "resolved1"."publicId" = @Agent)
            WHERE "disputes"."publicId" = @Id;
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
            WHERE "roles"."code" = 'audit_reader'
            """,
            new { Id = created.Id, Agent });
        // Losing current case eligibility hides the case before an evidence grant is considered.
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.GetEvidence(Agent, created.Id, ct));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.PreviewEvidence(Agent, created.Id, id, ct));
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.GrantEvidenceAccess(
            manager,
            created.Id,
            id,
            new(Agent, 24, "Audit identity"),
            ct));
        Assert.Equal(EvidencePng(), (await _repository.PreviewEvidence(Customer, created.Id, id, ct)).Content);
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.AddEvidence(
            Customer,
            created.Id,
            "bad-category.png",
            EvidencePng(),
            "Scanned",
            "Unknown",
            ct));
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "audit"."auditEvents" AS "admin_audit_logs"
            WHERE "admin_audit_logs"."action" = 'EvidenceAccessRevoked'
            AND "admin_audit_logs"."entityPublicId" = @Id
            """,
                new { Id = created.Id }));
    }

    [Fact]
    public async Task Evidence_is_case_scoped_encrypted_and_deduplicated()
    {
        var ct = CancellationToken.None;
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "Not recognised",
                Description = "Please investigate this purchase."
            },
            "evidence-case",
            ct);
        var png = EvidencePng();
        var id = await _repository.AddEvidence(Customer, created.Id, "C:\\camera\\receipt.png", png, "Unscanned", "Other", ct);
        Assert.Equal(id, await _repository.AddEvidence(Customer, created.Id, "same-photo.png", png, "Unscanned", "Other", ct));
        var files = await _repository.GetEvidence(Agent, created.Id, ct);
        Assert.Single(files);
        Assert.Equal("Restricted document", files[0].FileName);
        Assert.False(files[0].CanView);
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.DownloadEvidence(Agent, created.Id, id, ct));
        Assert.Equal(png, (await _repository.DownloadEvidence(Customer, created.Id, id, ct)).Content);
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.GetEvidence(OtherCustomer, created.Id, ct));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.DownloadEvidence(OtherCustomer, created.Id, id, ct));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.AddEvidence(
            OtherCustomer,
            created.Id,
            "proof.png",
            png,
            "Unscanned",
            "Other",
            ct));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.DownloadEvidence(Customer, created.Id, Guid.NewGuid(), ct));
        await using var c = await _connections.OpenConnection(ct);
        Assert.Equal(
            "binary:v1",
            await c.ExecuteScalarAsync<string>(
                """
            SELECT "dispute_evidence"."protectedContent" AS "protected_content"
            FROM "disputes"."evidence" AS "dispute_evidence"
            WHERE "dispute_evidence"."publicId" = @Id
            """,
                new { Id = id }));
        Assert.NotNull(await c.ExecuteScalarAsync<byte[]>(
            """
            SELECT "b"."protectedContent" AS "protected_content"
            FROM "disputes"."evidencePayloads" b
            JOIN "disputes"."evidence" e
            ON "e"."payloadId" = "b"."id"
            WHERE "e"."publicId" = @Id
            """,
            new { Id = id }));
        var audit = await c.ExecuteScalarAsync<string>(
            "SELECT " + DatabaseDialect.GetDatabaseDialect(_provider).AuditMetadataSelectExpression("\"safeMetadata\"") + """
            
            FROM "audit"."auditEvents" AS "admin_audit_logs"
            WHERE "admin_audit_logs"."entityPublicId" = @Id
            AND "admin_audit_logs"."action" = 'EvidenceAttached'
            """,
            new { Id = created.Id });
        Assert.DoesNotContain("receipt.png", audit!);
        await c.ExecuteAsync(
            """
            UPDATE "disputes"."disputes"
            SET "status" = 'Withdrawn'
            WHERE "disputes"."publicId" = @Id
            """,
            new { Id = created.Id });
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.AddEvidence(
            Customer,
            created.Id,
            "proof.png",
            png,
            "Unscanned",
            "Other",
            ct));
        Assert.Single(await _repository.GetEvidence(Customer, created.Id, ct));
    }

    [Fact]
    public async Task Evidence_limits_reject_unsupported_files_and_excess_attachments()
    {
        var ct = CancellationToken.None;
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "Not recognised",
                Description = "Please investigate this purchase."
            },
            "evidence-limits",
            ct);
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.AddEvidence(
            Customer,
            created.Id,
            "script.png",
            "<script>alert(1)</script>"u8.ToArray(),
            "Unscanned",
            "Other",
            ct));
        for (var i = 0; i < 10; i++)
            await _repository.AddEvidence(
            Customer,
            created.Id,
            "note-" + i + ".pdf",
            System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\nTest evidence " + i + "\n%%EOF"),
            "Unscanned",
            "Other",
            ct);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.AddEvidence(
            Customer,
            created.Id,
            "proof.png",
            EvidencePng(),
            "Unscanned",
            "Other",
            ct));
        Assert.Equal(10, (await _repository.GetEvidence(Customer, created.Id, ct)).Count);
    }

    [Fact]
    public async Task Identity_is_encrypted_masked_immutable_and_not_in_audit_metadata()
    {
        var ct = CancellationToken.None;
        const string identity = "9001155000085";
        await _repository.UpdateProfileDetails(Customer, new ProfileDetailsRequest("Alex", "Morgan", null, "Jordan"), ct);
        await _repository.SetIdentity(Customer, identity, 1900, ct);
        var profile = await _repository.GetProfile(Customer, ct);
        Assert.Equal("Jordan", profile.MiddleName);
        Assert.Equal("*********0085", profile.MaskedIdentityNumber);
        Assert.Equal(new DateTime(1990, 1, 15), profile.DateOfBirth);
        Assert.Null((await _repository.GetProfile(OtherCustomer, ct)).MaskedIdentityNumber);
        Assert.Equal(identity, await _repository.RevealIdentityNumber(Customer, ct));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.RevealIdentityNumber(OtherCustomer, ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.SetIdentity(Customer, "0002295000083", 2000, ct));
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.SetIdentity(OtherCustomer, "9001155000086", 1900, ct));
        await _repository.UpdateProfileDetails(Customer, new ProfileDetailsRequest("Alex", "Morgan", null, "Jordan"), ct);
        Assert.Equal("*********0085", (await _repository.GetProfile(Customer, ct)).MaskedIdentityNumber);
        await using var c = await _connections.OpenConnection(ct);
        Assert.StartsWith(
            "test:",
            await c.ExecuteScalarAsync<string>(
                """
            SELECT "customer_profiles"."protectedIdentityNumber" AS "protected_identity_number"
            FROM "identity"."customerProfiles" AS "customer_profiles"
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "customer_profiles"."userId") = @Customer
            """,
                new { Customer }));
        var audit = await c.ExecuteScalarAsync<string>(
            "SELECT " + DatabaseDialect.GetDatabaseDialect(_provider).AuditMetadataSelectExpression("\"safeMetadata\"") + """
            
            FROM "audit"."auditEvents" AS "admin_audit_logs"
            WHERE "admin_audit_logs"."entityPublicId" = @Customer
            AND "admin_audit_logs"."action" = 'IdentityDetailsRecorded'
            """,
            new { Customer });
        Assert.DoesNotContain(identity, audit!);
        var revealAudit = await c.ExecuteScalarAsync<string>(
            "SELECT " + DatabaseDialect.GetDatabaseDialect(_provider).AuditMetadataSelectExpression("\"safeMetadata\"") + """
            
            FROM "audit"."auditEvents" AS "admin_audit_logs"
            WHERE "admin_audit_logs"."entityPublicId" = @Customer
            AND "admin_audit_logs"."action" = 'IdentityNumberRevealed'
            """,
            new { Customer });
        Assert.DoesNotContain(identity, revealAudit!);
        await c.ExecuteAsync(
            """
            UPDATE "identity"."users"
            SET "accountStatus" = 'Disabled'
            WHERE "users"."publicId" = @Customer
            """,
            new { Customer });
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.RevealIdentityNumber(Customer, ct));
    }

    [Fact]
    public async Task Conversation_search_is_case_insensitive_literal_and_scoped_to_the_customer()
    {
        var ct = CancellationToken.None;
        var own = await _repository.SendMessage(
            Customer,
            new SupportSendRequest(null, null, "Statement 100% question", "Please help me understand my statement."),
            ct);
        await _repository.SendMessage(
            OtherCustomer,
            new SupportSendRequest(null, null, "Statement 100% question", "This belongs to a different customer."),
            ct);
        Assert.Equal(own, (await _repository.GetThreads(Customer, ct, search: "STATEMENT")).Single().Id);
        Assert.Equal(own, (await _repository.GetThreads(Customer, ct, search: "100%")).Single().Id);
        Assert.Empty(await _repository.GetThreads(Customer, ct, search: "missing subject"));
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.GetThreads(Customer, ct, search: new string('x', 101)));
    }

    [Fact]
    public async Task Creation_is_idempotent_owned_and_keeps_internal_notes_private()
    {
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "I did not make this transaction",
                Description = "I do not recognise this synthetic transaction."
            },
            "integration-key-0001",
            CancellationToken.None);
        Assert.Equal("Submitted", created.Status);
        Assert.NotNull(await _repository.GetCustomerDispute(Customer, created.Id, CancellationToken.None));
        Assert.Null(await _repository.GetCustomerDispute(OtherCustomer, created.Id, CancellationToken.None));
        Assert.Null(await _repository.GetTransaction(Customer, OtherTransaction, CancellationToken.None));
        var replay = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "I did not make this transaction",
                Description = "I do not recognise this synthetic transaction."
            },
            "integration-key-0001",
            CancellationToken.None);
        Assert.True(replay.Replayed);
        Assert.Equal(created.Id, replay.Id);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "I did not make this transaction",
                Description = "I do not recognise this synthetic transaction."
            },
            "integration-key-0002",
            CancellationToken.None));
        await _repository.AddNote(
            Agent,
            created.Id,
            new NoteCommand("Internal reviewer context.", "Internal"),
            "corr-test-1",
            CancellationToken.None);
        await _repository.AddNote(
            Agent,
            created.Id,
            new NoteCommand("We are reviewing your transaction.", "Customer"),
            "corr-test-2",
            CancellationToken.None);
        var customerView = await _repository.GetCustomerDispute(Customer, created.Id, CancellationToken.None);
        Assert.NotNull(customerView);
        Assert.Single(customerView.Notes);
        Assert.Equal("Customer", customerView.Notes[0].Visibility);
        var dispatcher = new DisputeService(_repository, new DisputePortal.Api.Repository.DisputeInputPolicyStore(_connections));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => dispatcher.Transition(
            OtherCustomer,
            created.Id,
            new TransitionCommand(2, "UnderReview", "Unauthorized review."),
            "corr-denied",
            CancellationToken.None));
        await dispatcher.Transition(
            Agent,
            created.Id,
            new TransitionCommand(2, "UnderReview", "Review started."),
            "corr-test-3",
            CancellationToken.None);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.TransitionDispute(
            Agent,
            created.Id,
            new TransitionCommand(1, "Resolved", "Stale change."),
            "corr-test-4",
            CancellationToken.None));
        var finalView = await _repository.GetCustomerDispute(Customer, created.Id, CancellationToken.None);
        Assert.NotNull(finalView);
        Assert.Equal("UnderReview", finalView.Dispute.Status);
        Assert.Equal(3, finalView.Dispute.Version);
        await using var connection = await _connections.OpenConnection(CancellationToken.None);
        Assert.Equal(
            6,
            await connection.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "audit"."auditEvents" AS "admin_audit_logs"
            WHERE "admin_audit_logs"."entityPublicId" = @Id
            """,
                new { Id = created.Id }));
    }

    [Fact]
    public async Task Submission_key_replays_only_the_same_business_request()
    {
        var ct = CancellationToken.None;
        var command = new CreateDisputeCommand
        {
            TransactionId = Transaction,
            Category = "Unrecognized",
            Reason = "I did not make this transaction",
            Description = "Please review this synthetic transaction."
        };
        var created = await _repository.CreateDispute(Customer, command, "bound-intake-key", ct);
        foreach (var changed in new[]
        {
            new global::DisputePortal.Api.Models.Domain.CreateDisputeCommand(command)
            {
                TransactionId = OtherTransaction
            },
            new global::DisputePortal.Api.Models.Domain.CreateDisputeCommand(command)
            {
                Category = "Other",
                Reason = "Other"
            },
            new global::DisputePortal.Api.Models.Domain.CreateDisputeCommand(command)
            {
                Reason = "Different reason"
            },
            new global::DisputePortal.Api.Models.Domain.CreateDisputeCommand(command)
            {
                Description = "Different supporting information."
            }
        }

        )
            await Assert.ThrowsAsync<PortalConflictException>(() => _repository.CreateDispute(Customer, changed, "bound-intake-key", ct));
        var replay = await _repository.CreateDispute(Customer, command, "bound-intake-key", ct);
        Assert.True(replay.Replayed);
        Assert.Equal(created.Id, replay.Id);
        var history = await _repository.GetCustomerDispute(Customer, created.Id, ct);
        Assert.NotNull(history);
        Assert.Single(history.History);
        await using var connection = await _connections.OpenConnection(ct);
        Assert.Equal(
            1,
            await connection.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "disputes"."disputes" AS "transaction_disputes"
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "transaction_disputes"."customerId") = @Customer
            AND "transaction_disputes"."idempotencyKey" = @Key
            """,
                new { Customer, Key = "bound-intake-key" }));
    }

    [Fact]
    public async Task Contact_codes_are_owned_expiring_attempt_limited_and_single_use()
    {
        var challenge = await _repository.ReserveContactChallenge(
            Customer,
            "phone",
            "+27821234567",
            "sms",
            "123456",
            CancellationToken.None);
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CompleteContactChange(
            Customer,
            challenge.Id,
            "123456",
            CancellationToken.None));
        await _repository.SetContactChallengeDelivery(Customer, challenge.Id, true, CancellationToken.None);
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CompleteContactChange(
            OtherCustomer,
            challenge.Id,
            "123456",
            CancellationToken.None));
        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CompleteContactChange(
            Customer,
            challenge.Id,
            "654321",
            CancellationToken.None));
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CompleteContactChange(
            Customer,
            challenge.Id,
            "123456",
            CancellationToken.None));
        await using var c = await _connections.OpenConnection(CancellationToken.None);
        Assert.Equal(
            5,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT "contact_verification_challenges"."attempts" AS "attempts"
            FROM "identity"."contactVerificationChallenges" AS "contact_verification_challenges"
            WHERE "contact_verification_challenges"."publicId" = @Id
            """,
                new { challenge.Id }));
        await c.ExecuteAsync(
            """
            UPDATE "identity"."contactVerificationChallenges"
            SET "createdAt" = @Old
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "contactVerificationChallenges"."userId") = @Customer
            """,
            new { Customer, Old = DateTimeOffset.UtcNow.AddMinutes(-2) });
        var second = await _repository.ReserveContactChallenge(
            Customer,
            "phone",
            "+27821234567",
            "whatsapp",
            "345678",
            CancellationToken.None);
        await _repository.SetContactChallengeDelivery(Customer, second.Id, true, CancellationToken.None);
        await c.ExecuteAsync(
            """
            UPDATE "identity"."contactVerificationChallenges"
            SET "expiresAt" = @Old
            WHERE "contactVerificationChallenges"."publicId" = @Id
            """,
            new { second.Id, Old = DateTimeOffset.UtcNow.AddSeconds(-1) });
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CompleteContactChange(
            Customer,
            second.Id,
            "345678",
            CancellationToken.None));
        await c.ExecuteAsync(
            """
            UPDATE "identity"."contactVerificationChallenges"
            SET "expiresAt" = @Future
            WHERE "contactVerificationChallenges"."publicId" = @Id
            """,
            new { second.Id, Future = DateTimeOffset.UtcNow.AddMinutes(1) });
        await _repository.CompleteContactChange(Customer, second.Id, "345678", CancellationToken.None);
        Assert.Equal("+27821234567", (await _repository.GetProfile(Customer, CancellationToken.None)).Phone);
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CompleteContactChange(
            Customer,
            second.Id,
            "345678",
            CancellationToken.None));
    }

    [Fact]
    public async Task Account_filters_and_support_threads_do_not_leak_between_customers()
    {
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "I did not make this transaction",
                Description = "Please investigate this purchase."
            },
            "scope-key-01",
            CancellationToken.None);
        Assert.Empty((await _repository.GetCustomerDisputes(Customer, 1, 20, CancellationToken.None, OtherAccount)).Items);
        Assert.Single((await _repository.GetCustomerDisputes(Customer, 1, 20, CancellationToken.None, Account)).Items);
        var assigned = await _repository.GetCase(Agent, created.Id, CancellationToken.None);
        Assert.Equal(Agent, assigned.AssignedTo);
        Assert.Equal("Fraud", assigned.QueueType);
        var thread = await _repository.SendMessage(
            Customer,
            new SupportSendRequest(null, created.Id, "Case question", "Can I send more information?"),
            CancellationToken.None);
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.GetMessages(OtherCustomer, thread, CancellationToken.None));
        await _repository.SendMessage(
            Agent,
            new SupportSendRequest(thread, null, "Case question", "Please reply here with details."),
            CancellationToken.None);
        Assert.Equal(2, (await _repository.GetMessages(Customer, thread, CancellationToken.None)).Count);
        Assert.NotNull((await _repository.GetCase(Customer, created.Id, CancellationToken.None)).FirstResponseAt);
    }

    [Fact]
    public async Task Registration_login_and_recovery_challenges_are_purpose_bound_and_single_use()
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new DisputePortal.Api.Models.Internal.RegistrationPayload(
            "Verified Customer",
            "verified@example.test",
            "email",
            "initial-test-hash",
            "Verified",
            "Customer"));
        var registration = await _repository.CreateIdentityChallenge(
            null,
            "Registration",
            "email",
            "verified@example.test",
            payload,
            "123456",
            CancellationToken.None);
        Assert.Null(await _repository.FindIdentity("verified@example.test", CancellationToken.None));
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CompleteRegistration(
            registration.ChallengeId,
            "123456",
            CancellationToken.None));
        await _repository.MarkIdentityChallengeReady(registration.ChallengeId, CancellationToken.None);
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CompleteLogin(
            registration.ChallengeId,
            "123456",
            CancellationToken.None));
        var user = await _repository.CompleteRegistration(registration.ChallengeId, "123456", CancellationToken.None);
        Assert.True((await _repository.FindIdentity("verified@example.test", CancellationToken.None))!.EmailVerified);
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CompleteRegistration(
            registration.ChallengeId,
            "123456",
            CancellationToken.None));
        var login = await _repository.CreateIdentityChallenge(
            user,
            "Login",
            "email",
            "verified@example.test",
            "",
            "234567",
            CancellationToken.None);
        await _repository.MarkIdentityChallengeReady(login.ChallengeId, CancellationToken.None);
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CompletePasswordReset(
            login.ChallengeId,
            "234567",
            "bad-purpose",
            CancellationToken.None));
        Assert.Equal(user, await _repository.CompleteLogin(login.ChallengeId, "234567", CancellationToken.None));
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CompleteLogin(
            login.ChallengeId,
            "234567",
            CancellationToken.None));
        var session = await _repository.CreateSession(user, DateTimeOffset.UtcNow.AddMinutes(30), CancellationToken.None);
        var recovery = await _repository.CreateIdentityChallenge(
            user,
            "Recovery",
            "email",
            "verified@example.test",
            "",
            "345678",
            CancellationToken.None);
        await _repository.MarkIdentityChallengeReady(recovery.ChallengeId, CancellationToken.None);
        await _repository.CompletePasswordReset(recovery.ChallengeId, "345678", "new-test-hash", CancellationToken.None);
        Assert.False(await _repository.IsSessionActive(session, DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.Equal("new-test-hash", (await _repository.FindLogin("VERIFIED@EXAMPLE.TEST", CancellationToken.None))!.PasswordHash);
    }

    [Fact]
    public async Task Password_reset_revokes_every_device_and_all_pending_security_challenges()
    {
        var ct = CancellationToken.None;
        var sessions = new[]
        {
            await _repository.CreateSession(Customer, DateTimeOffset.UtcNow.AddMinutes(30), ct),
            await _repository.CreateSession(Customer, DateTimeOffset.UtcNow.AddMinutes(30), ct),
            await _repository.CreateSession(Customer, DateTimeOffset.UtcNow.AddMinutes(30), ct)
        };
        var otherSession = await _repository.CreateSession(OtherCustomer, DateTimeOffset.UtcNow.AddMinutes(30), ct);
        var login = await _repository.CreateIdentityChallenge(Customer, "Login", "email", "customer@test.local", "", "123456", ct);
        await _repository.MarkIdentityChallengeReady(login.ChallengeId, ct);
        var contact = await _repository.ReserveContactChallenge(Customer, "phone", "+27821234567", "sms", "234567", ct);
        await _repository.SetContactChallengeDelivery(Customer, contact.Id, true, ct);
        var recovery = await _repository.CreateIdentityChallenge(Customer, "Recovery", "email", "customer@test.local", "", "345678", ct);
        await _repository.MarkIdentityChallengeReady(recovery.ChallengeId, ct);
        // Rejected reset attempts must not log out legitimate sessions.
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CompletePasswordReset(
            recovery.ChallengeId,
            "000000",
            "bad-hash",
            ct));
        foreach (var session in sessions)
            Assert.True(await _repository.IsSessionActive(session, DateTimeOffset.UtcNow, ct));
        await _repository.CompletePasswordReset(recovery.ChallengeId, "345678", "replacement-hash", ct);
        foreach (var session in sessions)
            Assert.False(await _repository.IsSessionActive(session, DateTimeOffset.UtcNow, ct));
        Assert.Empty(await _repository.GetActiveSessions(Customer, ct));
        Assert.True(await _repository.IsSessionActive(otherSession, DateTimeOffset.UtcNow, ct));
        Assert.Equal("replacement-hash", (await _repository.FindLogin("CUSTOMER@TEST.LOCAL", ct))!.PasswordHash);
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CompleteLogin(login.ChallengeId, "123456", ct));
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CompleteContactChange(Customer, contact.Id, "234567", ct));
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CompletePasswordReset(
            recovery.ChallengeId,
            "345678",
            "reused-hash",
            ct));
    }

    [Theory]
    [InlineData("email", "new@example.test", "email")]
    [InlineData("phone", "+27821234567", "sms")]
    [InlineData("phone", "+27821234567", "whatsapp")]
    public async Task Contact_verification_preserves_old_details_until_verified_then_revokes_old_access(
        string field,
        string destination,
        string channel)
    {
        var ct = CancellationToken.None;
        var before = await _repository.GetProfile(Customer, ct);
        var session = await _repository.CreateSession(Customer, DateTimeOffset.UtcNow.AddMinutes(30), ct);
        var login = await _repository.CreateIdentityChallenge(Customer, "Login", "email", "customer@test.local", "", "123456", ct);
        await _repository.MarkIdentityChallengeReady(login.ChallengeId, ct);
        var challenge = await _repository.ReserveContactChallenge(Customer, field, destination, channel, "234567", ct);
        await _repository.SetContactChallengeDelivery(Customer, challenge.Id, true, ct);
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CompleteContactChange(
            OtherCustomer,
            challenge.Id,
            "234567",
            ct));
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CompleteContactChange(Customer, challenge.Id, "000000", ct));
        var unchanged = await _repository.GetProfile(Customer, ct);
        Assert.Equal(before.Email, unchanged.Email);
        Assert.Equal(before.Phone, unchanged.Phone);
        await _repository.CompleteContactChange(Customer, challenge.Id, "234567", ct);
        var after = await _repository.GetProfile(Customer, ct);
        Assert.Equal(destination, field == "email" ? after.Email : after.Phone);
        var identity = await _repository.FindIdentity(destination, ct);
        Assert.NotNull(identity);
        Assert.Equal((channel, destination), identity.VerifiedContact()!.Value);
        Assert.False(await _repository.IsSessionActive(session, DateTimeOffset.UtcNow, ct));
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CompleteLogin(login.ChallengeId, "123456", ct));
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CompleteContactChange(Customer, challenge.Id, "234567", ct));
    }

    [Fact]
    public async Task Verified_phone_change_updates_a_phone_registered_login_identifier()
    {
        var ct = CancellationToken.None;
        var payload = System.Text.Json.JsonSerializer.Serialize(new DisputePortal.Api.Models.Internal.RegistrationPayload(
            "Phone Customer",
            "+27821110000",
            "sms",
            "initial-hash",
            "Phone",
            "Customer"));
        var registration = await _repository.CreateIdentityChallenge(null, "Registration", "sms", "+27821110000", payload, "123456", ct);
        await _repository.MarkIdentityChallengeReady(registration.ChallengeId, ct);
        var user = await _repository.CompleteRegistration(registration.ChallengeId, "123456", ct);
        var contact = await _repository.ReserveContactChallenge(user, "phone", "+27821110001", "whatsapp", "234567", ct);
        await _repository.SetContactChallengeDelivery(user, contact.Id, true, ct);
        await _repository.CompleteContactChange(user, contact.Id, "234567", ct);
        Assert.Null(await _repository.FindLogin("+27821110000", ct));
        Assert.Equal(user, (await _repository.FindLogin("+27821110001", ct))!.UserId);
        Assert.Equal(("whatsapp", "+27821110001"), (await _repository.FindIdentity("+27821110001", ct))!.VerifiedContact()!.Value);
    }

    [Fact]
    public async Task Only_start_and_final_milestones_are_queued_and_escalation_is_visible_without_internal_reason()
    {
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "I did not make this transaction",
                Description = "Please review this transaction."
            },
            "milestones-1",
            CancellationToken.None);
        await using var c = await _connections.OpenConnection(CancellationToken.None);
        await c.ExecuteAsync(
            """
            INSERT INTO "identity"."userRoles"("userId", "roleId")
            SELECT u."id", r."id"
            FROM "identity"."users" u
            CROSS JOIN "identity"."roles" r
            WHERE u."publicId" = @Supervisor
                AND r."code" = 'operations_manager'
            """,
            new { Supervisor = OtherCustomer });
        var initial = await c.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM "notifications"."deliveries" AS "notification_deliveries"
            WHERE(
                SELECT "related1"."publicId"
                FROM "disputes"."disputes" AS "related1"
                WHERE "related1"."id" = "notification_deliveries"."disputeId") = @Id
            """,
            new { created.Id });
        Assert.True(initial > 0);
        await _repository.TransitionDispute(
            Agent,
            created.Id,
            new TransitionCommand(2, "UnderReview", "Review has started."),
            "milestone-review",
            CancellationToken.None);
        Assert.Equal(
            initial,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "notifications"."deliveries" AS "notification_deliveries"
            WHERE(
                SELECT "related1"."publicId"
                FROM "disputes"."disputes" AS "related1"
                WHERE "related1"."id" = "notification_deliveries"."disputeId") = @Id
            """,
                new { created.Id }));
        await _repository.Escalate(
            Agent,
            created.Id,
            new EscalationRequest(3, "Internal sensitive reason for senior review."),
            CancellationToken.None);
        Assert.Equal(1, (await _repository.GetCase(Customer, created.Id, CancellationToken.None)).EscalationLevel);
        var visible = await _repository.GetCustomerDispute(Customer, created.Id, CancellationToken.None);
        Assert.Contains(visible!.Notes, n => n.Content.Contains("escalated"));
        Assert.DoesNotContain(visible.Notes, n => n.Content.Contains("Internal sensitive"));
        Assert.Equal(
            initial,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "notifications"."deliveries" AS "notification_deliveries"
            WHERE(
                SELECT "related1"."publicId"
                FROM "disputes"."disputes" AS "related1"
                WHERE "related1"."id" = "notification_deliveries"."disputeId") = @Id
            """,
                new { created.Id }));
        // Escalation removes ordinary specialist eligibility; senior review retains its own scope.
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.TransitionDispute(
            Agent,
            created.Id,
            new TransitionCommand(4, "Resolved", "Your case is complete."),
            "milestone-ineligible",
            CancellationToken.None));
        var seniorReview = await _repository.GetCase(OtherCustomer, created.Id, CancellationToken.None);
        Assert.Equal(4, seniorReview.Version);
        await _repository.TransitionDispute(
            OtherCustomer,
            created.Id,
            new TransitionCommand(seniorReview.Version, "Resolved", "Your case is complete."),
            "milestone-final",
            CancellationToken.None);
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
            new { created.Id }) > 0);
        var first = await _repository.ClaimNotifications(CancellationToken.None);
        var second = await _repository.ClaimNotifications(CancellationToken.None);
        Assert.Empty(first.Select(x => x.Id).Intersect(second.Select(x => x.Id)));
    }

    [Fact]
    public async Task Security_activity_and_session_revocation_are_owned_and_disabled_accounts_are_rejected()
    {
        var session = await _repository.CreateSession(Customer, DateTimeOffset.UtcNow.AddMinutes(30), CancellationToken.None);
        await _repository.RecordSecurityEvent(
            Customer,
            "SignIn",
            "Success",
            "127.0.0.1",
            "Mozilla/5.0 Windows Chrome/100",
            session,
            CancellationToken.None);
        Assert.Single(await _repository.GetSecurityActivity(Customer, 1, 20, CancellationToken.None));
        Assert.Empty(await _repository.GetSecurityActivity(OtherCustomer, 1, 20, CancellationToken.None));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.RevokeOwnedSession(
            OtherCustomer,
            session,
            CancellationToken.None));
        Assert.True(await _repository.IsSessionActive(session, DateTimeOffset.UtcNow, CancellationToken.None));
        await using var c = await _connections.OpenConnection(CancellationToken.None);
        await c.ExecuteAsync(
            """
            UPDATE "identity"."users"
            SET "accountStatus" = 'Disabled'
            WHERE "users"."publicId" = @Customer
            """,
            new { Customer });
        Assert.False(await _repository.IsSessionActive(session, DateTimeOffset.UtcNow, CancellationToken.None));
    }

    [Fact]
    public async Task Risk_checks_are_transaction_session_and_policy_bound_and_single_use()
    {
        var ct = CancellationToken.None;
        await using var c = await _connections.OpenConnection(ct);
        await c.ExecuteAsync(
            """
            UPDATE "transactions"."transactions"
            SET "amount" = 6000
            WHERE "transactions"."publicId" = @Id
            """,
            new { Id = Transaction });
        var command = new CreateDisputeCommand
        {
            TransactionId = Transaction,
            Category = "Unrecognized",
            Reason = "Not authorised",
            Description = "This is a security verification test."
        };
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.CreateDispute(Customer, command, "risk-test-1", ct));
        var session = await _repository.CreateSession(Customer, DateTimeOffset.UtcNow.AddMinutes(30), ct);
        var challenge = await _repository.ReserveDisputeVerification(
            Customer,
            session,
            Transaction,
            "email",
            "customer@test.local",
            "123456",
            ct);
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CompleteDisputeVerification(
            Customer,
            session,
            Transaction,
            challenge.ChallengeId,
            "123456",
            ct));
        await _repository.ReadyDisputeVerification(Customer, challenge.ChallengeId, ct);
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CompleteDisputeVerification(
            OtherCustomer,
            session,
            Transaction,
            challenge.ChallengeId,
            "123456",
            ct));
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CompleteDisputeVerification(
            Customer,
            session,
            OtherTransaction,
            challenge.ChallengeId,
            "123456",
            ct));
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CompleteDisputeVerification(
            Customer,
            session,
            Transaction,
            challenge.ChallengeId,
            "000000",
            ct));
        await _repository.CompleteDisputeVerification(Customer, session, Transaction, challenge.ChallengeId, "123456", ct);
        var verified = new global::DisputePortal.Api.Models.Domain.CreateDisputeCommand(command)
        {
            VerificationId = challenge.ChallengeId,
            SessionId = session
        };
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.CreateDispute(
            Customer,
            new global::DisputePortal.Api.Models.Domain.CreateDisputeCommand(verified) { SessionId = Guid.NewGuid() },
            "risk-test-1",
            ct));
        var created = await _repository.CreateDispute(Customer, verified, "risk-test-1", ct);
        Assert.Equal(
            "Used",
            await c.ExecuteScalarAsync<string>(
                """
            SELECT "dispute_verifications"."state" AS "state"
            FROM "identity"."disputeVerifications" AS "dispute_verifications"
            WHERE "dispute_verifications"."publicId" = @Id
            """,
                new { Id = challenge.ChallengeId }));
        Assert.Equal(created.Id, (await _repository.CreateDispute(Customer, verified, "risk-test-1", ct)).Id);
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.CompleteDisputeVerification(
            Customer,
            session,
            Transaction,
            challenge.ChallengeId,
            "123456",
            ct));
    }

    [Fact]
    public async Task Rules_require_super_admin_a_reason_and_current_version()
    {
        var ct = CancellationToken.None;
        var request = new RiskPolicyUpdateRequest(1, 1000, 20, 60, 800, "Tighten verification for testing.");
        await Assert.ThrowsAsync<PortalForbiddenException>(() => _repository.UpdateRiskPolicy(Customer, request, ct));
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
        await _repository.UpdateRiskPolicy(Agent, request, ct);
        Assert.Equal(2, (await _repository.GetRiskPolicy(ct)).Version);
        await _repository.UpdateRiskPolicy(Agent, request, ct); // exact request replay is idempotent
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "audit"."auditEvents" AS "admin_audit_logs"
            WHERE "admin_audit_logs"."action" = 'PolicyVersionPublished'
            """));
    }

    [Fact]
    public async Task Conversation_read_positions_are_owned_monotonic_and_resume_after_new_reply()
    {
        var ct = CancellationToken.None;
        var thread = await _repository.SendMessage(Customer, new SupportSendRequest(
            null,
            null,
            "A question",
            "Please assist with my account."), ct);
        var staff = await AddSupportStaff("dispute_agent");
        await _repository.AssignWaitingSupportConversations(ct);
        Assert.Equal(staff, (await _repository.GetSupportThread(staff, thread, ct))!.AssignedTo);
        await _repository.SendMessage(staff, new SupportSendRequest(thread, null, "A question", "We can help you here."), ct);
        var messages = await _repository.GetMessages(Customer, thread, ct);
        Assert.Equal(1, (await _repository.GetThreads(Customer, ct)).Single().UnreadCount);
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.MarkSupportRead(OtherCustomer, thread, messages.Last().Id, ct));
        await _repository.MarkSupportRead(Customer, thread, messages.Last().Id, ct);
        await _repository.MarkSupportRead(Customer, thread, messages.First().Id, ct);
        Assert.Equal(0, (await _repository.GetThreads(Customer, ct)).Single().UnreadCount);
        Assert.Equal(thread, await _repository.GetLastSupportThread(Customer, ct));
        await _repository.SendMessage(staff, new SupportSendRequest(thread, null, "A question", "A new reply for you."), ct);
        Assert.Equal(1, (await _repository.GetThreads(Customer, ct)).Single().UnreadCount);
    }

    [Fact]
    public async Task Bank_feed_imports_are_owned_idempotent_and_preserve_disputed_transactions()
    {
        var ct = CancellationToken.None;
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.LinkOwnedAccount(
            OtherCustomer,
            Account,
            "test-bank",
            "bank-account-1",
            ct));
        await _repository.LinkOwnedAccount(Customer, Account, "test-bank", "bank-account-1", ct);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.LinkOwnedAccount(
            OtherCustomer,
            OtherAccount,
            "test-bank",
            "bank-account-1",
            ct));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.GetOwnedBinding(OtherCustomer, Account, ct));
        var binding = (await _repository.GetOwnedBinding(Customer, Account, ct))!;
        var item = new BankTransaction
        {
            ExternalId = "bank-tx-1",
            OccurredAt = DateTimeOffset.UtcNow,
            Description = "A bank transaction",
            MerchantName = "Bank merchant",
            Amount = 125m,
            Currency = "ZAR",
            Type = "CardPurchase",
            Direction = "Debit",
            Status = "Pending",
            DisputeEligible = true
        };
        var pending = new BankTransactionPage
        {
            ExternalAccountId = "bank-account-1",
            Items = new[]
            {
                item
            },
            NextCursor = "cursor-1",
            HasMore = false
        };
        await _repository.ApplyPage(Customer, binding, pending, ct);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.ApplyPage(Customer, binding, pending, ct));
        await using var c = await _connections.OpenConnection(ct);
        var id = await c.ExecuteScalarAsync<Guid>(
            """
            SELECT(
                SELECT "related1"."publicId"
                FROM "transactions"."transactions" AS "related1"
                WHERE "related1"."id" = "bank_transaction_links"."transactionId") AS "transaction_id"
            FROM "transactions"."bankLinks" AS "bank_transaction_links"
            WHERE(
                SELECT "related2"."publicId"
                FROM "accounts"."accounts" AS "related2"
                WHERE "related2"."id" = "bank_transaction_links"."accountId") = @Account
            """,
            new { Account });
        Assert.False((await _repository.GetTransaction(Customer, id, ct))!.DisputeEligible);
        binding = (await _repository.GetOwnedBinding(Customer, Account, ct))!;
        var posted = new global::DisputePortal.Api.Models.Banking.BankTransactionPage(pending)
        {
            Items = new[]
            {
                new global::DisputePortal.Api.Models.Banking.BankTransaction(item)
                {
                    Status = "Posted"
                }
            },
            NextCursor = "cursor-2"
        };
        await _repository.ApplyPage(Customer, binding, posted, ct);
        Assert.True((await _repository.GetTransaction(Customer, id, ct))!.DisputeEligible);
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "transactions"."bankLinks" AS "bank_transaction_links"
            WHERE(
                SELECT "related1"."publicId"
                FROM "accounts"."accounts" AS "related1"
                WHERE "related1"."id" = "bank_transaction_links"."accountId") = @Account
            """,
                new { Account }));
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = id,
                Category = "Unrecognized",
                Reason = "Not recognised",
                Description = "A bank-feed dispute integration test."
            },
            "bank-feed-dispute-test",
            ct);
        binding = (await _repository.GetOwnedBinding(Customer, Account, ct))!;
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.ApplyPage(
            Customer,
            binding,
            new global::DisputePortal.Api.Models.Banking.BankTransactionPage(posted) { ExternalAccountId = "someone-else" },
            ct));
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.ApplyPage(
            Customer,
            binding,
            new global::DisputePortal.Api.Models.Banking.BankTransactionPage(posted)
            {
                Items = new[] { new global::DisputePortal.Api.Models.Banking.BankTransaction(item) {
                    Status = "Posted",
                    Amount = 900
                } },
                NextCursor = "cursor-3"
            },
            ct));
        Assert.Equal(125m, (await _repository.GetTransaction(Customer, id, ct))!.Amount);
        Assert.Equal(id, (await _repository.GetCustomerDispute(Customer, created.Id, ct))!.Dispute.TransactionId);
        Assert.Equal(binding.Revision, (await _repository.GetOwnedBinding(Customer, Account, ct))!.Revision);
        await _repository.ApplyPage(
            Customer,
            binding,
            new global::DisputePortal.Api.Models.Banking.BankTransactionPage(posted) { NextCursor = "cursor-3" },
            ct);
        Assert.Equal(id, (await _repository.GetCustomerDispute(Customer, created.Id, ct))!.Dispute.TransactionId);
    }

    [Fact]
    public async Task Case_conversations_have_case_context_and_grouped_history_keeps_account_scope()
    {
        var ct = CancellationToken.None;
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "Not recognised",
                Description = "A case title and scope test."
            },
            "case-context-test",
            ct);
        var thread = await _repository.SendMessage(
            Customer,
            new SupportSendRequest(null, created.Id, "Old generic subject", "I have a question about this case."),
            ct);
        var context = await _repository.GetSupportThread(Customer, thread, ct);
        Assert.Equal("Example Shop", context!.CaseTitle);
        Assert.Equal(created.Reference, context.Reference);
        Assert.NotNull(context.CaseCreatedAt);
        Assert.Null(await _repository.GetSupportThread(OtherCustomer, thread, ct));
        Assert.Equal(1, (await _repository.GetCustomerDisputes(Customer, 1, 12, ct, Account, "active")).Total);
        Assert.Equal(0, (await _repository.GetCustomerDisputes(Customer, 1, 12, ct, OtherAccount, "active")).Total);
        Assert.Equal(0, (await _repository.GetCustomerDisputes(Customer, 1, 12, ct, Account, "closed")).Total);
    }

    [Fact]
    public async Task Customer_case_search_respects_account_status_and_ownership()
    {
        var ct = CancellationToken.None;
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "Not recognised",
                Description = "A searchable case."
            },
            "case-search-test",
            ct);
        Assert.Equal(
            created.Id,
            Assert.Single((await _repository.GetCustomerDisputes(Customer, 1, 12, ct, Account, "active", "EXAMPLE SHOP")).Items).Id);
        Assert.Equal(1, (await _repository.GetCustomerDisputes(Customer, 1, 12, ct, search: created.Reference)).Total);
        Assert.Equal(0, (await _repository.GetCustomerDisputes(Customer, 1, 12, ct, OtherAccount, search: created.Reference)).Total);
        Assert.Equal(0, (await _repository.GetCustomerDisputes(OtherCustomer, 1, 12, ct, search: created.Reference)).Total);
        Assert.Equal(0, (await _repository.GetCustomerDisputes(Customer, 1, 12, ct, status: "closed", search: created.Reference)).Total);
        Assert.Equal(0, (await _repository.GetCustomerDisputes(Customer, 1, 12, ct, search: "nonexistent merchant")).Total);
    }

    [Theory]
    [InlineData("CardPurchase", "Card", "card_specialist")]
    [InlineData("DebitOrder", "DebitOrder", "debit_order_specialist")]
    [InlineData("Transfer", "Payments", "payments_specialist")]
    [InlineData("CashWithdrawal", "Payments", "payments_specialist")]
    public async Task Specialist_workflows_assign_only_qualified_staff_and_restrict_case_access(string type, string queue, string role)
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
                WHERE "resolved1"."publicId" = @Other),
            "roles"."id"
            FROM "identity"."roles" AS "roles"
            WHERE "roles"."code" = 'super_admin';
            UPDATE "transactions"."transactions"
            SET "transactionTypeId" =(
                SELECT "resolved2"."id"
                FROM "configuration"."transactionTypes" AS "resolved2"
                WHERE "resolved2"."code" = @Type)
            WHERE "transactions"."publicId" = @Transaction
            """,
            new
            {
                Other = OtherCustomer,
                Type = type,
                Transaction
            });
        var specialist = await _repository.CreateUser(
            OtherCustomer,
            new SaveUserRequest("specialist@test.local", "Specialist", role, "TestPassword!2026", FirstName: "Test", LastName: "Specialist"),
            "not-used",
            ct);
        await _repository.SetAvailability(specialist, "Available", ct);
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Duplicate",
                Reason = "I was charged more than once",
                Description = "A specialist review is needed."
            },
            "specialist-routing",
            ct);
        var item = await _repository.GetCase(specialist, created.Id, ct);
        Assert.Equal(queue, item.QueueType);
        Assert.Equal(specialist, item.AssignedTo);
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.GetCase(Agent, created.Id, ct));
        await Assert.ThrowsAsync<PortalNotFoundException>(() => _repository.AddNote(
            Agent,
            created.Id,
            new NoteCommand("Not my team's case.", "Internal"),
            "wrong-team",
            ct));
        await _repository.TransitionDispute(
            specialist,
            created.Id,
            new TransitionCommand(item.Version, "UnderReview", "Evidence review started."),
            "specialist-review",
            ct);
        var current = await _repository.GetCase(specialist, created.Id, ct);
        await _repository.TransitionDispute(
            specialist,
            created.Id,
            new TransitionCommand(current.Version, "AwaitingCustomer", "Please provide the relevant receipt or supporting evidence."),
            "request-evidence",
            ct);
        Assert.Equal("AwaitingCustomer", (await _repository.GetCustomerDispute(Customer, created.Id, ct))!.Dispute.Status);
        Assert.Equal(
            1,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "notifications"."notifications" AS "notifications"
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "notifications"."userId") = @User
            AND(
                SELECT "related2"."publicId"
                FROM "disputes"."disputes" AS "related2"
                WHERE "related2"."id" = "notifications"."disputeId") = @Id
            AND "notifications"."notificationType" = 'NewCase'
            """,
                new { User = specialist, Id = created.Id }));
    }

    [Fact]
    public async Task Recent_signins_are_five_successful_events_owned_by_the_customer()
    {
        var ct = CancellationToken.None;
        for (var i = 0; i < 8; i++)
            await _repository.RecordSecurityEvent(Customer, "SignIn", "Success", "127.0.0.1", "Windows Chrome/100", null, ct);
        await _repository.RecordSecurityEvent(Customer, "PasswordCheck", "Rejected", "127.0.0.1", "Windows Chrome/100", null, ct);
        await _repository.RecordSecurityEvent(OtherCustomer, "SignIn", "Success", "127.0.0.1", "Android Chrome/100", null, ct);
        var result = await _repository.GetRecentSignIns(Customer, ct);
        Assert.Equal(5, result.Count);
        Assert.All(
            result,
            item =>
        {
            Assert.Equal(Customer, item.UserId);
            Assert.Equal("SignIn", item.EventType);
            Assert.Equal("Success", item.Outcome);
        });
        Assert.Equal(result.OrderByDescending(item => item.OccurredAt).Select(item => item.Id), result.Select(item => item.Id));
    }

    [Fact]
    public async Task Development_seed_covers_all_transaction_types_and_is_repeatable()
    {
        var ct = CancellationToken.None;
        var migrator = new DatabaseInitializer(
            _connections,
            DatabaseDialect.GetDatabaseDialect(_provider),
            DisputePortal.TestFixtures.TestRuntimeConfiguration.Read());
        await SeedLegacyFixtures(ct);
        await using var c = await _connections.OpenConnection(ct);
        var count = await c.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "transactions"."transactions" AS "transactions"
            """);
        await SeedLegacyFixtures(ct);
        Assert.Equal(
            count,
            await c.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "transactions"."transactions" AS "transactions"
            """));
        var expectedTypes = new[]
        {
            "CardPurchase",
            "CreditCardPayment",
            "DebitOrder",
            "Transfer",
            "CashWithdrawal",
            "Deposit",
            "Fee",
            "Other",
            "Refund",
            "RefundReversal"
        };
        Assert.Equal(
            expectedTypes.Order(StringComparer.Ordinal),
            (await _repository.GetTransactionTypes(ct)).Select(type => type.TypeKey).Order(StringComparer.Ordinal));
        Assert.Equal(
            7,
            await c.ExecuteScalarAsync<int>("""
            SELECT COUNT(DISTINCT "type"."code")
            FROM "transactions"."transactions" AS "t"
            JOIN "configuration"."transactionTypes" AS "type"
                ON "type"."id" = "t"."transactionTypeId"
            """));
        Assert.Equal(
            3,
            await c.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "identity"."users" u
            JOIN "identity"."userRoles" ur
            ON "u"."id" = "ur"."userId"
            JOIN "identity"."roles" r
            ON "r"."id" = "ur"."roleId"
            WHERE "r"."code" IN('card_specialist',
                'debit_order_specialist',
                'payments_specialist')
            """));
    }

    [Fact]
    public async Task Profile_names_and_protected_address_are_owned_validated_and_not_written_to_audit_metadata()
    {
        var ct = CancellationToken.None;
        var address = new ResidentialAddress("12 Example Street", "Unit 4", "Example Suburb", "Johannesburg", "Gauteng", "2000", "ZA");
        await _repository.UpdateProfileDetails(Customer, new ProfileDetailsRequest("Alex", "Morgan", address), ct);
        var profile = await _repository.GetProfile(Customer, ct);
        Assert.Equal("Alex", profile.FirstName);
        Assert.Equal("Morgan", profile.LastName);
        Assert.Equivalent(address, profile.Address, strict: true);
        Assert.Null((await _repository.GetProfile(OtherCustomer, ct)).Address);
        await using var c = await _connections.OpenConnection(ct);
        Assert.StartsWith(
            "test:",
            await c.ExecuteScalarAsync<string>(
                """
            SELECT "customer_profiles"."protectedAddress" AS "protected_address"
            FROM "identity"."customerProfiles" AS "customer_profiles"
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "customer_profiles"."userId") = @Customer
            """,
                new { Customer }));
        var metadata = await c.ExecuteScalarAsync<string>(
            "SELECT " + DatabaseDialect.GetDatabaseDialect(_provider).AuditMetadataSelectExpression("\"safeMetadata\"") + """
            
            FROM "audit"."auditEvents" AS "admin_audit_logs"
            WHERE "admin_audit_logs"."entityPublicId" = @Customer
            AND "admin_audit_logs"."action" = 'ProfileDetailsChanged'
            """,
            new { Customer });
        Assert.DoesNotContain("Example Street", metadata!);
        Assert.DoesNotContain("Alex", metadata!);
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.UpdateProfileDetails(
            Customer,
            new ProfileDetailsRequest("", "Morgan", address),
            ct));
        await Assert.ThrowsAsync<PortalValidationException>(() => _repository.UpdateProfileDetails(
            Customer,
            new ProfileDetailsRequest("Alex", "Morgan", new ResidentialAddress(address) { City = "" }),
            ct));
        await _repository.UpdateProfileDetails(Customer, new ProfileDetailsRequest("Alex", "Morgan", null), ct);
        Assert.Null((await _repository.GetProfile(Customer, ct)).Address);
    }

    [Fact]
    public async Task Signin_details_are_owned_and_reflect_session_revocation()
    {
        var ct = CancellationToken.None;
        var session = await _repository.CreateSession(Customer, DateTimeOffset.UtcNow.AddMinutes(15), ct);
        await _repository.RecordSecurityEvent(Customer, "SignIn", "Success", "127.0.0.1", "Windows Chrome/120 Edg/120", session, ct);
        var item = Assert.Single(await _repository.GetRecentSignIns(Customer, ct));
        Assert.Null(await _repository.GetSignInDetail(OtherCustomer, item.Id, ct));
        var detail = await _repository.GetSignInDetail(Customer, item.Id, ct);
        Assert.Equal("Edge", detail!.Browser);
        Assert.Equal(session, detail.SessionId);
        Assert.True(detail.SessionActive);
        Assert.Equal("127.0.0.1", detail.IpAddress);
        Assert.Equal("Local development", detail.Location);
        await _repository.RevokeOwnedSession(Customer, session, ct);
        Assert.False((await _repository.GetSignInDetail(Customer, item.Id, ct))!.SessionActive);
        // Historical observations also survive when their session is unknown.
        var historicalSession = Guid.NewGuid();
        await _repository.RecordSecurityEvent(Customer, "SignIn", "Success", "127.0.0.1", "Historical session", historicalSession, ct);
        await using var c = await _connections.OpenConnection(ct);
        var historicalEvent = await c.ExecuteScalarAsync<Guid>(
            """
            SELECT "security_events"."publicId" AS "id"
            FROM "identity"."securityEvents" AS "security_events"
            WHERE "security_events"."observedSessionPublicId" = @Session
            """,
            new { Session = historicalSession });
        var historical = await _repository.GetSignInDetail(Customer, historicalEvent, ct);
        Assert.Equal(historicalSession, historical!.SessionId);
        Assert.False(historical.SessionActive);
    }

    public async Task DisposeAsync()
    {
        if (!string.IsNullOrWhiteSpace(_sqlAdmin))
        {
            // Only a uniquely generated test database is ever removed.
            if (!_databaseName.StartsWith("DisputePortal_Test_", StringComparison.Ordinal) || _databaseName.Length != 51)
            {
                throw new InvalidOperationException("Unexpected test database name.");
            }

            var settings = new SqlConnectionStringBuilder(_sqlAdmin)
            {
                InitialCatalog = "master"
            };
            SqlConnection.ClearAllPools();
            await using var c = new SqlConnection(settings.ConnectionString);
            await c.OpenAsync();
            await c.ExecuteAsync(
                $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}];",
                commandTimeout: DisputePortal.TestFixtures.TestRuntimeConfiguration.Read().MigrationCommandTimeoutSeconds);
        }
        else if (!string.IsNullOrWhiteSpace(_pgAdmin))
        {
            if (!_databaseName.StartsWith("DisputePortal_Test_", StringComparison.Ordinal) || _databaseName.Length != 51)
            {
                throw new InvalidOperationException("Unexpected test database name.");
            }

            NpgsqlConnection.ClearAllPools();
            var settings = new NpgsqlConnectionStringBuilder(_pgAdmin)
            {
                Database = "postgres"
            };
            await using var admin = new NpgsqlConnection(settings.ConnectionString);
            await admin.OpenAsync();
            await admin.ExecuteAsync(
                $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)",
                commandTimeout: DisputePortal.TestFixtures.TestRuntimeConfiguration.Read().MigrationCommandTimeoutSeconds);
        }
        else if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    private sealed class TestProtector : ISecretProtector
    {
        public string Protect(string value) => "test:" + value;

        public string Unprotect(string value) => value[5..];
    }
}
