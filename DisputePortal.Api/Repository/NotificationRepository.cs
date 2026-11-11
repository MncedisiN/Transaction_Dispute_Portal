using Dapper;
using DisputePortal.Api.Service.ExternalServices;
using System.Security.Cryptography;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;
using System.Text;
using System.Text.Json;
using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.ExternalServices;
using DisputePortal.Api.Interfaces.Security;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;

public class NotificationRepository(
    IDatabaseConnectionFactory factory,
    IDatabaseDialect databaseDialect,
    ISecretProtector secretProtector,
    IProfileRepository profiles,
    IIdentityVerificationRepository identities,
    DisputePortal.Api.Configuration.ApiRuntimeOptions runtime,
    IMessageTemplates? templates = null) : RepositoryBase(factory, databaseDialect), INotificationRepository, INotificationDeliveryRepository
{
    private static readonly string FeedScope = SqlScripts.ReadSqlScript("Shared/Queries/Api/NotificationRepository/FilterCustomerRelevantNotificationTypes");

    public async Task<PageResult<NotificationDto>> GetNotifications(
        Guid customerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var where = SqlScripts.ReadSqlScript("Shared/Queries/Api/NotificationRepository/JoinNotificationDetails");
        var args = new
        {
            CustomerId = customerId,
            Offset = PageOffset(page, pageSize),
            PageSize = pageSize
        };
        var select = SqlScripts.ReadSqlScript("Shared/Queries/Api/NotificationRepository/SelectNotificationColumns");
        await using var connection = await connections.OpenConnection(cancellationToken);
        using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/NotificationRepository/GetNotificationPage",
            ("SelectQuery", select),
            ("FilterClause", where),
            ("PaginationClause", dialect.PaginationClause),
            ("RelatedFilterClause", where)),
            args,
            cancellationToken: cancellationToken));
        var items = (await results.ReadAsync<NotificationDto>()).AsList();
        var total = await results.ReadSingleAsync<long>();
        // Dispose the grid reader before issuing the set-based enrichment query on SQL Server.
        results.Dispose();
        var recallText = await RefundRecallNotification.GetRefundRecallNotificationDetails(
            connection,
            null,
            dialect,
            items.Where(item => item.Type == "RefundRecalled" && item.DisputeId.HasValue).Select(item => item.DisputeId!.Value),
            templates ?? MessageTemplateCatalog.Default,
            cancellationToken);
        foreach (var item in items)
            if (item.Type == "RefundRecalled" && item.DisputeId is { } id && recallText.TryGetValue(id, out var body))
            {
                item.Body = body;
            }

        return new PageResult<NotificationDto>(items, page, pageSize, total);
    }

    public async Task<bool> MarkNotificationRead(Guid customerId, Guid notificationId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnection(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/NotificationRepository/UpdateNotificationsForCustomer",
            ("TrueValue", dialect.TrueLiteral)),
            new { NotificationId = notificationId, CustomerId = customerId },
            cancellationToken: cancellationToken)) == 1;
    }

    public async Task<string> GetNotificationPreference(Guid user, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        return await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/NotificationRepository/GetCustomerProfileForUser"),
            new { User = user },
            cancellationToken: ct)) ?? "inapp";
    }

    public async Task SaveNotificationPreference(Guid user, string channel, CancellationToken ct)
    {
        if (channel is not ("inapp" or "email" or "sms" or "whatsapp"))
        {
            throw new PortalValidationException("Select a notification channel.");
        }

        var identity = await profiles.GetProfile(user, ct);
        var verified = await identities.FindIdentity(string.IsNullOrEmpty(identity.Email) ? identity.Phone ?? "" : identity.Email, ct);
        if ((channel == "email"
            && verified?.EmailVerified != true)
            || ((channel is "sms" or "whatsapp")
            && verified?.PhoneVerified != true))
        {
            throw new PortalConflictException("Verify that contact in your profile first.");
        }

        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/NotificationRepository/DeletePreferencesForUser"),
            new { User = user, Channel = channel },
            tx,
            cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    public async Task SavePushSubscription(Guid user, Guid session, PushSubscriptionRequest subscription, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/NotificationRepository/CountUserSessionsForUser"),
            new
            {
                Id = session,
                User = user,
                Now = DateTimeOffset.UtcNow
            },
            tx,
            cancellationToken: ct)) != 1)
        {
            throw new PortalForbiddenException();
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subscription.Endpoint)));
        // Reassigning a browser subscription to its current session prevents cross-account delivery on shared devices.
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/NotificationRepository/DeletePushSubscriptionsForUserAndSessionByHashAndUserAndSessionAndIdAndPayload"),
            new
            {
                Id = Guid.NewGuid(),
                User = user,
                Session = session,
                Hash = hash,
                Payload = secretProtector.Protect(JsonSerializer.Serialize(subscription)),
                Now = DateTimeOffset.UtcNow
            },
            tx,
            cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    public async Task RemovePushSubscriptions(Guid user, Guid session, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/NotificationRepository/DeletePushSubscriptionsForUserAndSessionByUserAndSession"),
            new { User = user, Session = session },
            cancellationToken: ct));
    }

    public async Task DeletePushSubscription(Guid id, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await c.ExecuteAsync(new CommandDefinition(
            """
            DELETE
            FROM "notifications"."pushSubscriptions"
            WHERE "pushSubscriptions"."publicId" = @Id
            """,
            new { Id = id },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<StoredPushSubscription>> GetPushSubscriptions(Guid user, CancellationToken ct, Guid? session = null)
    {
        // Background delivery outlives normal web-session expiry. Explicit logout,
        // password reset and session revocation still disable the subscription.
        await using var c = await connections.OpenConnection(ct);
        var rows = await c.QueryAsync<PushRow>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/NotificationRepository/GetActivePushSubscriptionsForUserAndSession"),
            new { User = user, Session = session },
            cancellationToken: ct));
        return rows.Select(row =>
        {
            var value = JsonSerializer.Deserialize<PushSubscriptionRequest>(secretProtector.Unprotect(row.Payload))!;
            return new StoredPushSubscription(row.Id, value.Endpoint, value.P256dh, value.Auth);
        }).ToArray();
    }

    public async Task<IReadOnlyList<NotificationDelivery>> ClaimNotifications(CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        var now = DateTimeOffset.UtcNow;
        // A crashed worker may already have sent the external message. Preserve
        // that ambiguous outcome for review instead of automatically sending twice.
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/NotificationRepository/MarkExpiredDeliveryLeaseForReview"),
            new { Now = now },
            tx,
            cancellationToken: ct));
        var rows = (await c.QueryAsync<NotificationDelivery>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/NotificationRepository/GetPendingDeliveries"),
            new { Now = now },
            tx,
            cancellationToken: ct))).AsList();
        await c.ExecuteAsync(new CommandDefinition(
            """
            UPDATE "notifications"."events"
            SET
                "state" = 'Review',
                "lastError" = 'delivery_outcome_unknown',
                "completedAt" = @Now,
                "leaseUntil" = NULL
            WHERE "events"."state" = 'Processing'
            AND "events"."leaseUntil" < @Now
            """,
            new { Now = now },
            tx,
            cancellationToken: ct));
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/NotificationRepository/SkipNotificationForChangedRecipientScope",
            ("ReadScope", WorkflowAccess.ReadScope("u.\"publicId\""))),
            new { Now = now },
            tx,
            cancellationToken: ct));
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/NotificationRepository/SkipInternalProjectionNotifications"),
            new { Now = now },
            tx,
            cancellationToken: ct));
        var events = await c.QueryAsync<NotificationDelivery>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/NotificationRepository/GetPendingEvents",
            ("ReadScope", WorkflowAccess.ReadScope("u.\"publicId\""))),
            new { Now = now },
            tx,
            cancellationToken: ct));
        foreach (var item in events)
        {
            await c.ExecuteAsync(new CommandDefinition(
                """
                UPDATE "notifications"."events"
                SET
                    "state" = 'Processing',
                    "attempts" = "events"."attempts" + 1,
                    "leaseUntil" = @Lease
                WHERE "events"."publicId" = @Id
                """,
                new { item.Id, Lease = now.AddMinutes(runtime.NotificationLeaseMinutes) },
                tx,
                cancellationToken: ct));
            item.Milestone = "Event:" + item.Milestone;
            rows.Add(item);
        }

        foreach (var row in rows.Where(value => !value.Milestone.StartsWith("Event:", StringComparison.Ordinal)))
            await c.ExecuteAsync(new CommandDefinition(
            """
            UPDATE "notifications"."deliveries"
            SET
                "state" = 'Processing',
                "attempts" = "deliveries"."attempts" + 1,
                "leaseUntil" = @Lease
            WHERE "deliveries"."publicId" = @Id
            """,
            new { row.Id, Lease = now.AddMinutes(runtime.NotificationLeaseMinutes) },
            tx,
            cancellationToken: ct));
        await tx.CommitAsync(ct);
        return rows;
    }

    public async Task<NotificationTarget> GetNotificationTarget(Guid user, string channel, CancellationToken ct)
    {
        var profile = await profiles.GetProfile(user, ct);
        var identity = await identities.FindIdentity(string.IsNullOrEmpty(profile.Email) ? profile.Phone ?? "" : profile.Email, ct);
        var preferred = await GetNotificationPreference(user, ct);
        var allowed = channel == preferred && (channel == "email" ? identity?.EmailVerified == true : identity?.PhoneVerified == true);
        return new(channel, channel == "email" ? identity?.Email : identity?.Phone, allowed);
    }

    public async Task CompleteNotification(Guid id, string outcome, string? error, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        var parameters = new
        {
            Id = id,
            State = outcome,
            Error = error,
            Now = DateTimeOffset.UtcNow
        };
        var affected = await c.ExecuteAsync(new CommandDefinition(
            """
            UPDATE "notifications"."deliveries"
            SET
                "state" = @State,
                "lastError" = @Error,
                "completedAt" = @Now,
                "leaseUntil" = NULL
            WHERE "deliveries"."publicId" = @Id
            """,
            parameters,
            cancellationToken: ct));
        if (affected == 0)
        {
            await c.ExecuteAsync(new CommandDefinition(
                """
                UPDATE "notifications"."events"
                SET
                    "state" = @State,
                    "lastError" = @Error,
                    "completedAt" = @Now,
                    "leaseUntil" = NULL
                WHERE "events"."publicId" = @Id
                """,
                parameters,
                cancellationToken: ct));
        }
    }

    public async Task<long> GetUnreadNotificationCount(Guid user, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        return await c.ExecuteScalarAsync<long>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/NotificationRepository/CountNotificationsForUser",
            ("FalseValue", dialect.FalseLiteral)),
            new { User = user },
            cancellationToken: ct));
    }

    public async Task<PushNotificationContext?> GetPushContext(Guid user, Guid delivery, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        var row = await c.QuerySingleOrDefaultAsync<PushNotificationContext>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/NotificationRepository/GetOwnedNotificationDeliveryContextAndUnreadCount",
            ("FalseValue", dialect.FalseLiteral),
            ("ReadScope", WorkflowAccess.ReadScope("@User"))),
            new { User = user, Delivery = delivery },
            cancellationToken: ct));
        if (row?.Kind == "RefundRecalled" && row.DisputeId is { } dispute)
        {
            var text = await RefundRecallNotification.GetRefundRecallNotificationDetails(
                c,
                null,
                dialect,
                [dispute],
                templates ?? MessageTemplateCatalog.Default,
                ct);
            if (text.TryGetValue(dispute, out var body))
            {
                row.Body = body;
            }
        }

        return row;
    }

    private class PushRow
    {
        public Guid Id { get; set; }
        public string Payload { get; set; } = string.Empty;

        public PushRow()
        {
        }

        public PushRow(Guid Id, string Payload)
        {
            this.Id = Id;
            this.Payload = Payload;
        }
    }
}
