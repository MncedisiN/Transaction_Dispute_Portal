using DisputePortal.Api.Repository;
using System.Security.Cryptography;
using Dapper;
using DisputePortal.Api.Service.Security;
using DisputePortal.Api.Service.Disputes;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using Xunit;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Security;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Expired_delivery_lease_is_retained_for_review_and_never_blindly_resent()
    {
        var ct = CancellationToken.None;
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "I did not make this transaction",
                Description = "Please investigate this payment."
            },
            "push-unknown-outcome",
            ct);
        await using var c = await _connections.OpenConnection(ct);
        var expired = await c.ExecuteScalarAsync<Guid>(
            """
            SELECT "notification_deliveries"."publicId" AS "id"
            FROM "notifications"."deliveries" AS "notification_deliveries"
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "notification_deliveries"."userId") = @Customer
            AND(
                SELECT "related2"."publicId"
                FROM "disputes"."disputes" AS "related2"
                WHERE "related2"."id" = "notification_deliveries"."disputeId") = @Id
            AND "notification_deliveries"."channel" = 'push'
            """,
            new { Customer, Id = created.Id });
        await c.ExecuteAsync(
            """
            UPDATE "notifications"."deliveries"
            SET "state" = 'Processing',
            "attempts" = 1,
            "leaseUntil" = @Expired
            WHERE "deliveries"."publicId" = @Id
            """,
            new { Id = expired, Expired = DateTimeOffset.UtcNow.AddMinutes(-1) });
        var claimed = await _repository.ClaimNotifications(ct);
        Assert.DoesNotContain(claimed, row => row.Id == expired);
        Assert.NotEmpty(claimed);
        Assert.Equal(
            "Review",
            await c.ExecuteScalarAsync<string>(
                """
            SELECT "notification_deliveries"."state" AS "state"
            FROM "notifications"."deliveries" AS "notification_deliveries"
            WHERE "notification_deliveries"."publicId" = @Id
            """,
                new { Id = expired }));
        Assert.NotEmpty((await c.ExecuteScalarAsync<string>(
            """
            SELECT "notification_deliveries"."lastError" AS "last_error"
            FROM "notifications"."deliveries" AS "notification_deliveries"
            WHERE "notification_deliveries"."publicId" = @Id
            """,
            new { Id = expired }))!);
    }

    [Fact]
    public async Task Push_subscription_survives_normal_session_expiry_but_stops_on_revocation()
    {
        var ct = CancellationToken.None;
        var session = await _repository.CreateSession(Customer, DateTimeOffset.UtcNow.AddHours(1), ct);
        using var agreement = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var key = agreement.ExportParameters(false);
        static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await _repository.SavePushSubscription(
            Customer,
            session,
            new PushSubscriptionRequest(
                "https://fcm.googleapis.com/fcm/send/lifecycle-test",
                Encode([4, .. key.Q.X!, .. key.Q.Y!]),
                Encode(RandomNumberGenerator.GetBytes(16))),
            ct);
        await using var c = await _connections.OpenConnection(ct);
        var expired = DateTimeOffset.UtcNow.AddMinutes(-1);
        // Retain a valid past session lifetime while modelling natural expiry.
        await c.ExecuteAsync(
            """
            UPDATE "identity"."userSessions"
            SET "createdAt" = @Created,
            "lastActivityAt" = @Activity,
            "expiresAt" = @Expired
            WHERE "userSessions"."publicId" = @Id
            """,
            new
            {
                Id = session,
                Created = expired.AddHours(-1),
                Activity = expired.AddMinutes(-1),
                Expired = expired
            });
        Assert.Single(await _repository.GetPushSubscriptions(Customer, ct));
        Assert.Single(await _repository.GetPushSubscriptions(Customer, ct, session));
        Assert.Empty(await _repository.GetPushSubscriptions(Customer, ct, Guid.NewGuid()));
        Assert.Empty(await _repository.GetPushSubscriptions(OtherCustomer, ct));
        await _repository.RevokeSession(session, DateTimeOffset.UtcNow, ct);
        Assert.Empty(await _repository.GetPushSubscriptions(Customer, ct));
    }

    [Fact]
    public async Task Realtime_security_checks_are_owner_bound_and_do_not_extend_idle_activity()
    {
        var ct = CancellationToken.None;
        var policy = new SessionPolicy();
        var session = await _repository.CreateSession(Customer, DateTimeOffset.UtcNow.AddHours(1), ct);
        var reader = new RealtimeSessionReader(_connections, policy, DisputePortal.TestFixtures.TestRuntimeConfiguration.Read());
        await using var c = await _connections.OpenConnection(ct);
        var now = DateTimeOffset.UtcNow;
        // Use database-rounded seconds so the unchanged-time assertion is portable.
        var activity = DateTimeOffset.FromUnixTimeSeconds(now.AddMinutes(-1).ToUnixTimeSeconds());
        await c.ExecuteAsync(
            """
            UPDATE "identity"."userSessions"
            SET "lastActivityAt" = @Activity
            WHERE "userSessions"."publicId" = @Id
            """,
            new { Activity = activity, Id = session });
        Assert.True(await reader.IsActive(Customer, session, now, ct));
        Assert.False(await reader.IsActive(OtherCustomer, session, now, ct));
        Assert.Equal(
            activity,
            await c.ExecuteScalarAsync<DateTimeOffset>(
                """
            SELECT "user_sessions"."lastActivityAt" AS "last_activity_at"
            FROM "identity"."userSessions" AS "user_sessions"
            WHERE "user_sessions"."publicId" = @Id
            """,
                new { Id = session }));
        var stale = now.AddMinutes(-policy.IdleMinutes - 1);
        await c.ExecuteAsync(
            """
            UPDATE "identity"."userSessions"
            SET "lastActivityAt" = @Activity
            WHERE "userSessions"."publicId" = @Id
            """,
            new { Activity = stale, Id = session });
        Assert.False(await reader.IsActive(Customer, session, now, ct));
    }

    [Fact]
    public async Task Automatic_cookie_checks_do_not_extend_idle_but_user_actions_do_and_cannot_revive_expiry()
    {
        var ct = CancellationToken.None;
        var policy = new SessionPolicy();
        var session = await _repository.CreateSession(Customer, DateTimeOffset.UtcNow.AddHours(1), ct);
        var reader = new RealtimeSessionReader(_connections, policy, DisputePortal.TestFixtures.TestRuntimeConfiguration.Read());
        using var services = new ServiceCollection().AddSingleton<IAuthenticationRepository>(_repository).AddSingleton<IRealtimeSessionReader>(reader).BuildServiceProvider();
        CookieValidatePrincipalContext Context(string path, string activity)
        {
            var http = new DefaultHttpContext
            {
                RequestServices = services
            };
            http.Request.Path = path;
            http.Request.Headers["X-Portal-Activity"] = activity;
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Customer.ToString()), new Claim("session_id", session.ToString())],
                "test"));
            return new(
                http,
                new AuthenticationScheme("Cookies", null, typeof(CookieAuthenticationHandler)),
                new CookieAuthenticationOptions(),
                new AuthenticationTicket(principal, "Cookies"));
        }

        await using var c = await _connections.OpenConnection(ct);
        var activity = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds());
        await c.ExecuteAsync(
            """
            UPDATE "identity"."userSessions"
            SET "lastActivityAt" = @Activity
            WHERE "userSessions"."publicId" = @Id
            """,
            new { Id = session, Activity = activity });
        foreach (var path in new[]
        {
            "/api/v1/auth/me",
            "/api/v1/auth/csrf",
            "/api/v1/realtime/negotiate",
            "/api/v1/disputes"
        }

        )
            for (var i = 0; i < 3; i++)
                await PortalCookieSessionValidation.ValidateCookieSession(Context(path, "0"));
        Assert.Equal(
            activity,
            await c.ExecuteScalarAsync<DateTimeOffset>(
                """
            SELECT "user_sessions"."lastActivityAt" AS "last_activity_at"
            FROM "identity"."userSessions" AS "user_sessions"
            WHERE "user_sessions"."publicId" = @Id
            """,
                new { Id = session }));
        var explicitAction = Context("/api/v1/disputes", "1");
        await PortalCookieSessionValidation.ValidateCookieSession(explicitAction);
        Assert.NotNull(explicitAction.Principal);
        Assert.True(await c.ExecuteScalarAsync<DateTimeOffset>(
            """
            SELECT "user_sessions"."lastActivityAt" AS "last_activity_at"
            FROM "identity"."userSessions" AS "user_sessions"
            WHERE "user_sessions"."publicId" = @Id
            """,
            new { Id = session }) > activity);
        await c.ExecuteAsync(
            """
            UPDATE "identity"."userSessions"
            SET "lastActivityAt" = @Expired
            WHERE "userSessions"."publicId" = @Id
            """,
            new { Id = session, Expired = DateTimeOffset.UtcNow.AddMinutes(-policy.IdleMinutes - 1) });
        var idle = Context("/api/v1/disputes", "1");
        await PortalCookieSessionValidation.ValidateCookieSession(idle);
        Assert.Null(idle.Principal);
        await c.ExecuteAsync(
            """
            UPDATE "identity"."userSessions"
            SET "lastActivityAt" = @Now,
            "createdAt" = @Expired
            WHERE "userSessions"."publicId" = @Id
            """,
            new
            {
                Id = session,
                Now = DateTimeOffset.UtcNow,
                Expired = DateTimeOffset.UtcNow.AddMinutes(-policy.AbsoluteMinutes - 1)
            });
        var absolute = Context("/api/v1/disputes", "1");
        await PortalCookieSessionValidation.ValidateCookieSession(absolute);
        Assert.Null(absolute.Principal);
    }
}
