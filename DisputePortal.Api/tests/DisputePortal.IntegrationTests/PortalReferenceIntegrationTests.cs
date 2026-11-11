using DisputePortal.Api.Models.Domain;
using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Api.Service.Disputes;
using DisputePortal.Api.Controllers;
using DisputePortal.Api.Models.Internal;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Xunit;
using Dapper;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Exceptions;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Portal_interface_settings_are_retained_audited_and_require_a_current_admin_version()
    {
        var ct = CancellationToken.None;
        var store = new DisputePortal.Api.Repository.PortalSettingsRepository(
            _connections,
            DisputePortal.Api.Repository.Data.DatabaseDialect.GetDatabaseDialect(_provider));
        var initial = await store.GetPortalSettings(ct);
        Assert.False(initial.Settings.CustomerSearchEnabled);
        Assert.False(initial.Settings.CustomerAccountOpeningEnabled);
        var serviceClient = new AccountOpeningServiceClient();
        using var services = new ServiceCollection()
            .AddSingleton<IPortalSettingsRepository>(store)
            .AddSingleton<IServiceClient>(serviceClient)
            .BuildServiceProvider();
        var accountController = new PlatformController(services, new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Platform:Enabled"] = "true" }).Build())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var accountRequest = new OpenAccountRequest { RequestId = Guid.NewGuid(), Type = "Savings" };
        await Assert.ThrowsAsync<PortalForbiddenException>(() => accountController.Accounts(accountRequest, ct));
        Assert.Equal(0, serviceClient.RequestCount);
        var request = new UpdatePortalSettingsRequest
        {
            ExpectedVersion = initial.Version,
            Reason = "Update the retained interface wording.",
            Settings = initial.Settings
        };
        request.Settings.CustomerSearchEnabled = true;
        request.Settings.CustomerAccountOpeningEnabled = true;
        request.Settings.Queues["General"].Label = "Case Assistance";
        request.Settings.EnquiryFlow.Steps.Single(step => step.Kind == "description").Prompt = "Describe the issue for our support team.";
        await Assert.ThrowsAsync<PortalForbiddenException>(() => store.PublishPortalSettings(Customer, request, ct));
        await using var connection = await _connections.OpenConnection(ct);
        await connection.ExecuteAsync("""
            INSERT INTO "identity"."userRoles" (
                "userId",
                "roleId"
            )
            SELECT
                u."id",
                r."id"
            FROM "identity"."users" u
            CROSS JOIN "identity"."roles" r
            WHERE u."publicId" = @Admin
            AND r."code" = 'super_admin'
            """, new { Admin = OtherCustomer });
        var published = await store.PublishPortalSettings(OtherCustomer, request, ct);
        Assert.Equal(initial.Version + 1, published.Version);
        var current = await store.GetPortalSettings(ct);
        Assert.True(current.Settings.CustomerSearchEnabled);
        Assert.True(current.Settings.CustomerAccountOpeningEnabled);
        Assert.IsType<OkObjectResult>(await accountController.Accounts(accountRequest, ct));
        Assert.Equal(1, serviceClient.RequestCount);
        var retained = await connection.QuerySingleAsync<string>("""
            SELECT "body"
            FROM "configuration"."portalSettings"
            WHERE "publicId" = @Id
            """, new { Id = initial.PublicId });
        using var retainedSettings = JsonDocument.Parse(retained);
        Assert.False(retainedSettings.RootElement.GetProperty("customerSearchEnabled").GetBoolean());
        Assert.False(retainedSettings.RootElement.GetProperty("customerAccountOpeningEnabled").GetBoolean());
        Assert.Equal("Case Assistance", current.Settings.Queues["General"].Label);
        Assert.Equal("Describe the issue for our support team.", current.Settings.EnquiryFlow.Steps.Single(step => step.Kind == "description").Prompt);
        Assert.Equal(initial.Version + 1, await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "configuration"."portalSettings"
            WHERE "code" = 'Interface'
            """));
        await Assert.ThrowsAsync<PortalConflictException>(() => store.PublishPortalSettings(OtherCustomer, request, ct));
        var audit = await _repository.GetAuditLogs(1, 20, "PortalSettingsPublished", ct);
        Assert.Single(audit.Items);
    }

    [Fact]
    public async Task Portal_reference_reads_database_roles_without_exposing_user_data()
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(
            AppContext.BaseDirectory,
            "Configuration",
            "portal-reference.json")).Build();
        var controller = new PortalReferenceController(
            _connections,
            new DisputePortal.Api.Repository.PortalSettingsRepository(
                _connections,
                DisputePortal.Api.Repository.Data.DatabaseDialect.GetDatabaseDialect(_provider)),
            _repository,
            new DisputePortal.Api.Repository.CaseStatusRepository(
            _connections,
            DisputePortal.Api.Repository.Data.DatabaseDialect.GetDatabaseDialect(_provider)),
            new DisputePortal.Api.Repository.EvidencePolicyRepository(_connections));
        var result = Assert.IsType<OkObjectResult>(await controller.GetPortalReferenceData(CancellationToken.None));
        var json = JsonSerializer.SerializeToElement(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.True(json.GetProperty("roles").TryGetProperty("customer", out _));
        Assert.True(json.GetProperty("statuses").GetProperty("Withdrawn").GetProperty("closed").GetBoolean());
        Assert.Equal(5, json.GetProperty("assignmentReason").GetProperty("minimum").GetInt32());
        Assert.Equal(128, json.GetProperty("password").GetProperty("maximum").GetInt32());
        var evidence = json.GetProperty("evidence");
        Assert.Equal((await ReadEvidenceSettings()).MaximumFiles, evidence.GetProperty("maximumFiles").GetInt32());
        Assert.Equal((await ReadEvidenceSettings()).MaximumFileBytes, evidence.GetProperty("maximumFileBytes").GetInt32());
        Assert.Equal((await ReadEvidenceSettings()).Categories.Count, evidence.GetProperty("categories").GetArrayLength());
        Assert.DoesNotContain("Withdrawn", evidence.GetProperty("uploadStatuses").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(2, json.GetProperty("supportBody").GetProperty("minimum").GetInt32());
        Assert.Equal(2000, json.GetProperty("supportBody").GetProperty("maximum").GetInt32());
        Assert.Equal(1000, json.GetProperty("additionalDetails").GetProperty("maximum").GetInt32());
        Assert.Equal(50, json.GetProperty("staffCapacity").GetProperty("maximum").GetInt32());
        Assert.True(json.GetProperty("transactionTypes").TryGetProperty("CreditCardPayment", out _));
        Assert.Equal("Completed", json.GetProperty("transactionStatuses").GetProperty("Posted").GetString());
        Assert.Equal(24, json.GetProperty("evidenceDefaultHours").GetInt32());
        Assert.Equal(5, json.GetProperty("evidenceReason").GetProperty("minimum").GetInt32());
        Assert.DoesNotContain("loginIdentifier", json.GetRawText());
        Assert.DoesNotContain("passwordHash", json.GetRawText());
    }

    [Fact]
    public async Task Case_actions_are_enriched_for_customer_detail_and_bounded_staff_queue()
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
            "frontend-actions",
            ct);
        var customer = await _repository.GetCase(Customer, created.Id, ct);
        Assert.True(customer.Actions.CanWithdraw);
        Assert.False(customer.Actions.CanManage);
        Assert.Empty(customer.Actions.Assignees);
        var staff = await _repository.GetCase(Agent, created.Id, ct);
        Assert.True(staff.Actions.Claim.Allowed);
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
            Agent);
        Assert.True(Assert.Single(queue.Items, row => row.Dispute.Id == created.Id).Operations!.Actions.Claim.Allowed);
        var internalRead = await _repository.GetAdminQueue(
            new AdminQueueFilter
            {
                Page = 1,
                PageSize = 20,
                Search = null,
                Status = null,
                Priority = null
            },
            ct);
        Assert.Empty(internalRead.Items);
    }
    private sealed class AccountOpeningServiceClient : IServiceClient
    {
        public int RequestCount { get; private set; }

        public Task<T> SendServiceRequest<T>(
            string service,
            HttpMethod method,
            string path,
            object? body = null,
            ClaimsPrincipal? actor = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Assert.Equal("accounts", service);
            Assert.Equal(HttpMethod.Post, method);
            Assert.Equal("/accounts", path);
            Assert.IsType<OpenAccountRequest>(body);
            RequestCount++;
            return Task.FromResult((T)(object)JsonSerializer.SerializeToElement(new { accepted = true }));
        }
    }
}
