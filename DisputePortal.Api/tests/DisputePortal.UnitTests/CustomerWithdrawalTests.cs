using System.Reflection;
using System.Security.Claims;
using DisputePortal.Api.Controllers;
using DisputePortal.Api.Middleware;
using DisputePortal.Api.Models.Validators;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Service.Disputes;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Interfaces.Disputes;

namespace DisputePortal.UnitTests;

public sealed class CustomerWithdrawalTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Invalid_version_cannot_reach_storage(int version)
    {
        var repository = Substitute.For<IDisputeRepository>();
        var dispatcher = new DisputeService(repository, new DisputePortal.TestFixtures.DisputeInputFixture());
        var exception = await Assert.ThrowsAsync<PortalValidationException>(() => dispatcher.Withdraw(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new WithdrawDisputeCommand(version),
            "withdraw-test",
            CancellationToken.None));
        Assert.Equal("expectedVersion", exception.Field);
        Assert.Empty(repository.ReceivedCalls());
    }

    [Fact]
    public async Task Withdrawal_passes_the_authenticated_customer_version_correlation_and_cancellation_to_storage()
    {
        var repository = Substitute.For<IDisputeRepository>();
        var dispatcher = new DisputeService(repository, new DisputePortal.TestFixtures.DisputeInputFixture());
        var customer = Guid.NewGuid();
        var dispute = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        repository.WithdrawCustomerDispute(customer, dispute, 3, "withdraw-test", cancellation.Token).Returns(4);
        Assert.Equal(4, await dispatcher.Withdraw(customer, dispute, new WithdrawDisputeCommand(3), "withdraw-test", cancellation.Token));
        await repository.Received(1).WithdrawCustomerDispute(customer, dispute, 3, "withdraw-test", cancellation.Token);
    }

    [Fact]
    public async Task Endpoint_uses_session_identity_and_returns_the_updated_customer_view()
    {
        var customer = Guid.NewGuid();
        var dispute = Guid.NewGuid();
        var repository = Substitute.For<IDisputeRepository>();
        var dispatcher = Substitute.For<IDisputeService>();
        var summary = new DisputeSummaryDto(
            dispute,
            "DP-test",
            Guid.NewGuid(),
            "Payment",
            10,
            "ZAR",
            "Other",
            "Withdrawn",
            2,
            4,
            DateTimeOffset.UtcNow,
            null);
        var updated = new DisputeDetailDto(summary, null, "Merchant", DateTimeOffset.UtcNow, [], []);
        using var cancellation = new CancellationTokenSource();
        repository.GetCustomerDispute(customer, dispute, cancellation.Token).Returns(updated);
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, customer.ToString())], "test"))
        };
        context.Items["CorrelationId"] = "withdraw-test";
        var controller = new DisputesController(repository, dispatcher, new CreateDisputeValidator())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = context
            }
        };
        var result = Assert.IsType<OkObjectResult>(await controller.Withdraw(dispute, new WithdrawDisputeRequest(3), cancellation.Token));
        Assert.Same(updated, result.Value);
        await dispatcher.Received(1).Withdraw(
            customer,
            dispute,
            Arg.Is<WithdrawDisputeCommand>(command => command.ExpectedVersion == 3),
            "withdraw-test",
            cancellation.Token);
        Assert.Contains(typeof(DisputesController).GetCustomAttributes<AuthorizeAttribute>(), attribute => attribute.Policy == "customer");
    }

    [Fact]
    public async Task Withdrawal_cookie_request_rejects_invalid_csrf_before_reaching_the_action()
    {
        var nextCalled = false;
        var middleware = new CsrfValidationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var problems = new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider().GetRequiredService<IProblemDetailsService>();
        var context = WithdrawalContext();
        var feature = Substitute.For<IAntiforgeryValidationFeature>();
        feature.IsValid.Returns(false);
        context.Features.Set(feature);
        await middleware.Invoke(context, problems, Substitute.For<IAntiforgery>());
        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task Withdrawal_cookie_request_with_valid_csrf_can_reach_the_action()
    {
        var nextCalled = false;
        var middleware = new CsrfValidationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var problems = new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider().GetRequiredService<IProblemDetailsService>();
        var context = WithdrawalContext();
        await middleware.Invoke(context, problems, Substitute.For<IAntiforgery>());
        Assert.True(nextCalled);
    }

    private static DefaultHttpContext WithdrawalContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = $"/api/v1/disputes/{Guid.NewGuid()}/withdraw";
        context.Response.Body = new MemoryStream();
        return context;
    }
}
