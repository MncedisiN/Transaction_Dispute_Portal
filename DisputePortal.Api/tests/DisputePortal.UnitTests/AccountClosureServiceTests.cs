using DisputePortal.Api.Models.Security;
using System.Security.Claims;
using DisputePortal.Api.Controllers;
using DisputePortal.Api.Models;
using DisputePortal.Api.Service.Security;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Service.Accounts;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Accounts;
using DisputePortal.Api.Interfaces.Security;

namespace DisputePortal.UnitTests;

public sealed class AccountClosureServiceTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("different-user")]
    [InlineData("wrong-password")]
    public async Task Password_and_ownership_failures_never_create_or_deliver_a_challenge(string failure)
    {
        var fixture = new Fixture();
        if (failure == "missing")
        {
            fixture.Authentication.FindLogin(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<LoginRecord?>(null));
        }
        else if (failure == "different-user")
        {
            fixture.Authentication.FindLogin(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new LoginRecord(fixture.Login) { UserId = Guid.NewGuid() });
        }
        else
        {
            fixture.Passwords.Verify(fixture.UserId, "hash", "current").Returns(false);
        }

        var error = await Assert.ThrowsAsync<PortalValidationException>(() => fixture.Service.StartAccountClosure(
            fixture.UserId,
            new("current"),
            CancellationToken.None));
        Assert.Equal("password", error.Field);
        Assert.Empty(fixture.Identity.ReceivedCalls());
        Assert.Empty(fixture.Delivery.ReceivedCalls());
        Assert.Empty(fixture.Accounts.ReceivedCalls());
        if (failure != "wrong-password")
        {
            Assert.Empty(fixture.Passwords.ReceivedCalls());
        }
    }

    [Fact]
    public async Task Unverified_contact_never_creates_or_delivers_a_challenge()
    {
        var fixture = new Fixture();
        fixture.Identity.FindIdentity(fixture.Login.LoginIdentifier, Arg.Any<CancellationToken>()).Returns(new VerifiedIdentity
        {
            Id = fixture.UserId,
            Email = "customer@example.test"
        });
        var error = await Assert.ThrowsAsync<PortalValidationException>(() => fixture.Service.StartAccountClosure(
            fixture.UserId,
            new("current"),
            CancellationToken.None));
        Assert.Equal("contact", error.Field);
        Assert.Empty(fixture.Delivery.ReceivedCalls());
        Assert.Empty(fixture.Accounts.ReceivedCalls());
    }

    [Fact]
    public async Task Unavailable_delivery_is_rejected_before_reserving_a_challenge()
    {
        var fixture = new Fixture();
        fixture.Delivery.IsAvailable("email").Returns(false);
        await Assert.ThrowsAsync<VerificationUnavailableException>(() => fixture.Service.StartAccountClosure(
            fixture.UserId,
            new("current"),
            CancellationToken.None));
        Assert.Empty(fixture.Accounts.ReceivedCalls());
        await fixture.Delivery.DidNotReceive().SendVerificationCode(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<VerificationPurpose>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("email")]
    [InlineData("sms")]
    [InlineData("whatsapp")]
    public async Task Owned_password_sends_the_reserved_code_to_the_verified_contact_before_enabling_it(string channel)
    {
        var fixture = new Fixture(channel);
        using var cancellation = new CancellationTokenSource();
        var challenge = await fixture.Service.StartAccountClosure(fixture.UserId, new("current"), cancellation.Token);
        Assert.Same(fixture.Challenge, challenge);
        Assert.Matches("^[1-9][0-9]{5}$", fixture.ReservedCode!);
        Assert.Equal(new[] { "reserve", "send", "ready" }, fixture.Events);
        fixture.Passwords.Received(1).Verify(fixture.UserId, "hash", "current");
        await fixture.Authentication.Received(1).FindLogin("CUSTOMER@EXAMPLE.TEST", cancellation.Token);
        await fixture.Accounts.Received(1).ReserveDeletionChallenge(
            fixture.UserId,
            fixture.Destination,
            fixture.ReservedCode!,
            cancellation.Token);
        await fixture.Delivery.Received(1).SendVerificationCode(
            channel,
            fixture.Destination,
            fixture.ReservedCode!,
            5,
            VerificationPurpose.AccountDeletion,
            cancellation.Token);
        await fixture.Accounts.Received(1).SetDeletionChallengeReady(fixture.UserId, challenge.Id, true, cancellation.Token);
    }

    [Fact]
    public async Task Cancelled_delivery_disables_the_reserved_challenge_even_after_the_request_disconnects()
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Delivery.SendVerificationCode(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<VerificationPurpose>(),
            Arg.Any<CancellationToken>()).Returns(_ =>
        {
            cancellation.Cancel();
            return Task.FromCanceled(cancellation.Token);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.StartAccountClosure(
            fixture.UserId,
            new("current"),
            cancellation.Token));
        await fixture.Accounts.Received(1).SetDeletionChallengeReady(fixture.UserId, fixture.Challenge.Id, false, CancellationToken.None);
        await fixture.Accounts.DidNotReceive().SetDeletionChallengeReady(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            true,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ready_failure_disables_the_reserved_challenge_and_preserves_the_failure()
    {
        var fixture = new Fixture();
        var failure = new InvalidOperationException("Readiness update failed.");
        fixture.Accounts.SetDeletionChallengeReady(fixture.UserId, fixture.Challenge.Id, true, Arg.Any<CancellationToken>()).Returns(Task.FromException(failure));
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.StartAccountClosure(
            fixture.UserId,
            new("current"),
            CancellationToken.None));
        Assert.Same(failure, actual);
        await fixture.Accounts.Received(1).SetDeletionChallengeReady(fixture.UserId, fixture.Challenge.Id, false, CancellationToken.None);
    }

    [Theory]
    [InlineData(PasswordVerificationResult.Failed, false)]
    [InlineData(PasswordVerificationResult.Success, true)]
    [InlineData(PasswordVerificationResult.SuccessRehashNeeded, true)]
    public void Password_adapter_keeps_identity_hash_verification_semantics(PasswordVerificationResult verification, bool accepted)
    {
        var userId = Guid.NewGuid();
        var hasher = Substitute.For<IPasswordHasher<PortalPasswordUser>>();
        hasher.VerifyHashedPassword(Arg.Is<PortalPasswordUser>(user => user.Id == userId), "hash", "current").Returns(verification);
        Assert.Equal(accepted, new PortalPasswordVerifier(hasher).Verify(userId, "hash", "current"));
    }

    [Fact]
    public async Task Controller_preserves_the_field_validation_response()
    {
        var userId = Guid.NewGuid();
        var service = Substitute.For<IAccountClosureService>();
        service.StartAccountClosure(userId, Arg.Any<StartDeletionRequest>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<DeletionChallengeDto>(new PortalValidationException(
            "Check your current password.",
            "password")));
        var controller = new AccountManagementController(Substitute.For<IAccountManagementRepository>(), service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test"))
                }
            }
        };
        var result = Assert.IsType<UnprocessableEntityObjectResult>(await controller.RequestAccountClosure(
            new("wrong"),
            CancellationToken.None));
        var problem = Assert.IsType<ValidationProblemDetails>(result.Value);
        Assert.Equal("Check your current password.", Assert.Single(problem.Errors["password"]));
        Assert.Equal(422, problem.Status);
    }

    private sealed class Fixture
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public IAccountManagementRepository Accounts { get; } = Substitute.For<IAccountManagementRepository>();
        public IProfileRepository Profiles { get; } = Substitute.For<IProfileRepository>();
        public IAuthenticationRepository Authentication { get; } = Substitute.For<IAuthenticationRepository>();
        public IIdentityVerificationRepository Identity { get; } = Substitute.For<IIdentityVerificationRepository>();
        public IVerificationDelivery Delivery { get; } = Substitute.For<IVerificationDelivery>();
        public IPasswordVerifier Passwords { get; } = Substitute.For<IPasswordVerifier>();
        public DeletionChallengeDto Challenge { get; } = new(Guid.NewGuid(), "masked", DateTimeOffset.UtcNow.AddMinutes(5));
        public List<string> Events { get; } = [];
        public LoginRecord Login { get; }
        public string Destination { get; }
        public string? ReservedCode { get; private set; }
        public AccountClosureService Service { get; }

        public Fixture(string channel = "email")
        {
            Login = new(UserId, "customer@example.test", "hash", "Active", "Customer", ["customer"], []);
            Destination = channel == "email" ? "verified@example.test" : "+27821234567";
            Profiles.GetProfile(UserId, Arg.Any<CancellationToken>()).Returns(new ProfileDto
            {
                Id = UserId,
                Email = "customer@example.test"
            });
            Authentication.FindLogin(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Login);
            Passwords.Verify(UserId, "hash", "current").Returns(true);
            Identity.FindIdentity(Login.LoginIdentifier, Arg.Any<CancellationToken>()).Returns(new VerifiedIdentity
            {
                Id = UserId,
                Email = "verified@example.test",
                EmailVerified = true,
                Phone = "+27821234567",
                PhoneVerified = true,
                PreferredChannel = channel
            });
            Delivery.IsAvailable(channel).Returns(true);
            Accounts.ReserveDeletionChallenge(UserId, Destination, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                ReservedCode = call.ArgAt<string>(2);
                Events.Add("reserve");
                return Task.FromResult(Challenge);
            });
            Delivery.SendVerificationCode(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<VerificationPurpose>(),
                Arg.Any<CancellationToken>()).Returns(_ =>
            {
                Events.Add("send");
                return Task.CompletedTask;
            });
            Accounts.SetDeletionChallengeReady(UserId, Challenge.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                Events.Add(call.ArgAt<bool>(2) ? "ready" : "failed");
                return Task.CompletedTask;
            });
            Service = new(Accounts, Profiles, Authentication, Identity, Delivery, Passwords);
        }
    }
}
