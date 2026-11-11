using Dapper;
using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Exceptions;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    [Fact]
    public async Task Assessment_registration_keeps_contact_unverified_and_emits_one_registration_event()
    {
        var registration = new RegistrationPayload(
            "Assessment Customer", "assessment.customer@example.test", "email",
            "synthetic-password-hash", "Assessment", "Customer",
            new RegistrationDetails("ZA", "assessment.customer@example.test", "+27820000011",
                "ZA", "national-id", "ZA", "9502185589088", null, "ZAR") { DateOfBirth = new DateOnly(1995, 2, 18) });
        var id = await _repository.RegisterWithoutContactVerification(registration, default);
        var identity = await _repository.FindIdentity(registration.Contact, default);
        Assert.NotNull(identity);
        Assert.Equal(id, identity.Id);
        Assert.Null(identity.VerifiedContact());
        Assert.False(identity.RequiresSecondFactor);
        await Assert.ThrowsAsync<PortalConflictException>(() =>
            _repository.RegisterWithoutContactVerification(registration, default));
        await using var connection = await _connections.OpenConnection(default);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM "identity"."users"
            WHERE "publicId" = @Id
            """, new { Id = id }));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM service_outbox WHERE event_type = 'user.registered'"));
    }
}
