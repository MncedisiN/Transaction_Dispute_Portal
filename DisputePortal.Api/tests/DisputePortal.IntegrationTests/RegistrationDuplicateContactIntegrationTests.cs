using Dapper;
using System.Text.Json;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Internal;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Registration_contact_check_counts_shared_staff_contacts_and_normalizes_legacy_phone_formats()
    {
        var ct = CancellationToken.None;
        await using var c = await _connections.OpenConnection(ct);
        await c.ExecuteAsync(
            """
            UPDATE "identity"."customerProfiles"
            SET "contactLoginEnabled" = @Disabled,
            "email" = @Email,
            "phone" = @Phone
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "customerProfiles"."userId") IN(@First,
                @Second)
            """,
            new
            {
                Disabled = false,
                Email = "shared.staff@example.test",
                Phone = "082 123-4567",
                First = Customer,
                Second = OtherCustomer
            });
        Assert.True(await _repository.RegistrationContactExists("  SHARED.STAFF@EXAMPLE.TEST ", ct));
        Assert.True(await _repository.RegistrationContactExists("+27821234567", ct));
        Assert.True(await _repository.RegistrationContactExists("0027 82 123 4567", ct));
        Assert.False(await _repository.RegistrationContactExists("available@example.test", ct));
        var before = await c.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "identity"."identityChallenges" AS "identity_challenges"
            """);
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.CreateIdentityChallenge(
            null,
            "Registration",
            "email",
            "SHARED.STAFF@example.test",
            "",
            "123456",
            ct));
        Assert.Equal(
            before,
            await c.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM "identity"."identityChallenges" AS "identity_challenges"
            """));
    }

    [Fact]
    public async Task Registration_rechecks_contact_inside_the_final_transaction_after_the_initial_check()
    {
        var ct = CancellationToken.None;
        const string destination = "registration.race@example.test";
        Assert.False(await _repository.RegistrationContactExists(destination, ct));
        var payload = JsonSerializer.Serialize(new RegistrationPayload(
            "Test Customer",
            destination,
            "email",
            "synthetic-password-hash",
            "Test",
            "Customer"));
        var challenge = await _repository.CreateIdentityChallenge(null, "Registration", "email", destination, payload, "123456", ct);
        await _repository.MarkIdentityChallengeReady(challenge.ChallengeId, ct);
        await using var c = await _connections.OpenConnection(ct);
        await c.ExecuteAsync(
            """
            UPDATE "identity"."customerProfiles"
            SET "contactLoginEnabled" = @Disabled,
            "email" = @Email
            WHERE(
                SELECT "related1"."publicId"
                FROM "identity"."users" AS "related1"
                WHERE "related1"."id" = "customerProfiles"."userId") = @User
            """,
            new
            {
                Disabled = false,
                Email = destination,
                User = OtherCustomer
            });
        await Assert.ThrowsAsync<PortalConflictException>(() => _repository.CompleteRegistration(challenge.ChallengeId, "123456", ct));
        Assert.Equal(
            0,
            await c.ExecuteScalarAsync<int>(
                """
            SELECT COUNT(*)
            FROM "identity"."users" AS "users"
            WHERE "users"."normalizedLoginIdentifier" = @Contact
            """,
                new { Contact = destination.ToUpperInvariant() }));
        Assert.Equal(
            "Ready",
            await c.ExecuteScalarAsync<string>(
                """
            SELECT "identity_challenges"."state" AS "state"
            FROM "identity"."identityChallenges" AS "identity_challenges"
            WHERE "identity_challenges"."publicId" = @Id
            """,
                new { Id = challenge.ChallengeId }));
    }
}
