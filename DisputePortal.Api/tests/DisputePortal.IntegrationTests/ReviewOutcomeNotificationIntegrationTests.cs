using DisputePortal.Api.Platform;
using DisputePortal.Api.Repository;
using DisputePortal.Api.Repository.Data;
using DisputePortal.Api.Service.ExternalServices;
using DisputePortal.Platform.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.ExternalServices;
using DisputePortal.Api.Interfaces.Security;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Review_outcome_push_is_durable_requestor_only_and_deduplicated_on_either_provider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFinanceRepository>(Finance());
        services.AddSingleton<ISecretProtector>(new TestProtector());
        services.AddSingleton<IMessageTemplates>(MessageTemplateCatalog.Default);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ReviewOutcomes:already_refunded"] = "The original payment has already been refunded. A second refund is not allowed." }).Build());
        await using var provider = services.BuildServiceProvider();
        var remote = new ProjectionAccounts(new AccountRecord
        {
            Id = Account,
            UserId = Customer,
            Number = "1234567890",
            Type = "Cheque",
            CountryCode = "ZA",
            Currency = "ZAR",
            Status = "Active",
            LedgerBalance = 0,
            ReservedBalance = 0,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow
        });
        var consumer = new IdentityProjectionConsumer(ProjectionDatabase(), provider.GetRequiredService<IServiceScopeFactory>(), remote);
        var request = Guid.NewGuid();
        var value = DomainEvent.CreateDomainEvent(
            "scheduling",
            "reversal.review.completed",
            new ReversalReviewOutcome
            {
                RequestId = request,
                ActorId = Agent,
                OriginalTransactionId = Transaction,
                Kind = "Refund",
                Status = "Ineligible",
                Code = "already_refunded"
            },
            request);
        await consumer.Handle(value, default);
        await consumer.Handle(value, default);
        await consumer.Handle(new global::DisputePortal.Platform.Contracts.DomainEvent(value) { EventId = Guid.NewGuid() }, default);
        var notifications = new NotificationRepository(
            _connections,
            DatabaseDialect.GetDatabaseDialect(_provider),
            new TestProtector(),
            _repository,
            _repository, DisputePortal.TestFixtures.TestRuntimeConfiguration.Read());
        var item = Assert.Single(
            (await notifications.GetNotifications(Agent, 1, 100, default)).Items,
            n => n.Type == "RefundReviewIneligible");
        Assert.Contains("already been refunded", item.Body);
        Assert.Equal("/admin/dispute-operations?review=" + request, item.Url);
        Assert.DoesNotContain(
            (await notifications.GetNotifications(Customer, 1, 100, default)).Items,
            n => n.Type.StartsWith("RefundReview"));
        var delivery = Assert.Single(
            await notifications.ClaimNotifications(default),
            n => n.UserId == Agent && n.Milestone == "Event:RefundReviewIneligible");
        Assert.Equal("push", delivery.Channel);
        Assert.Equal(item.Body, (await notifications.GetPushContext(Agent, delivery.Id, default))!.Body);
        Assert.Null(await notifications.GetPushContext(Customer, delivery.Id, default));
    }
}
