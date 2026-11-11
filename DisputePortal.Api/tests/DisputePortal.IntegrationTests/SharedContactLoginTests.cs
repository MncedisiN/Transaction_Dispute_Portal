using Dapper;
using Xunit;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    private async Task SharedStaffContacts()
    {
        await using var c = await _connections.OpenConnection(CancellationToken.None);
        await c.ExecuteAsync(
            """
      UPDATE "identity"."customerProfiles"
      SET "email" = 'customer@test.local',
      "emailVerified" = @Yes,
      "phone" = '+27821234567',
      "phoneVerified" = @Yes
      WHERE(
          SELECT "related1"."publicId"
          FROM "identity"."users" AS "related1"
          WHERE "related1"."id" = "customerProfiles"."userId") = @Customer;
      UPDATE "identity"."users"
      SET "loginIdentifier" = 'superadmin',
      "normalizedLoginIdentifier" = 'SUPERADMIN'
      WHERE "users"."publicId" = @Staff;
      UPDATE "identity"."customerProfiles"
      SET "contactLoginEnabled" = @No,
      "email" = 'customer@test.local',
      "emailVerified" = @Yes,
      "phone" = '+27821234567',
      "phoneVerified" = @Yes
      WHERE(
          SELECT "related2"."publicId"
          FROM "identity"."users" AS "related2"
          WHERE "related2"."id" = "customerProfiles"."userId") = @Staff
      """,
            new
            {
                Customer,
                Staff = Agent,
                Yes = true,
                No = false
            });
    }

    [Fact]
    public async Task Shared_contacts_route_customer_contact_login_and_staff_username_to_separate_identities()
    {
        await SharedStaffContacts();
        var ct = CancellationToken.None;
        Assert.Equal(Agent, (await _repository.FindLogin("SUPERADMIN", ct))!.UserId);
        Assert.Equal(Customer, (await _repository.FindLogin("CUSTOMER@TEST.LOCAL", ct))!.UserId);
        Assert.Equal(Customer, (await _repository.FindLogin("+27821234567", ct))!.UserId);
        var staff = (await _repository.FindIdentity("superadmin", ct))!;
        Assert.Equal(Agent, staff.Id);
        Assert.True(staff.EmailVerified);
        Assert.True(staff.PhoneVerified);
        Assert.Equal(Customer, (await _repository.FindIdentity("customer@test.local", ct))!.Id);
        Assert.Equal(Customer, (await _repository.FindIdentity("+27821234567", ct))!.Id);
    }

    [Theory]
    [InlineData("email")]
    [InlineData("phone")]
    public async Task Shared_staff_contact_verification_preserves_the_username_and_customer_profile(string field)
    {
        await SharedStaffContacts();
        var ct = CancellationToken.None;
        var destination = field == "email" ? "customer@test.local" : "+27821234567";
        var channel = field == "email" ? "email" : "sms";
        var challenge = await _repository.ReserveContactChallenge(Agent, field, destination, channel, "123456", ct);
        await _repository.SetContactChallengeDelivery(Agent, challenge.Id, true, ct);
        await _repository.CompleteContactChange(Agent, challenge.Id, "123456", ct);
        Assert.Equal(Agent, (await _repository.FindLogin("SUPERADMIN", ct))!.UserId);
        Assert.Equal(Customer, (await _repository.FindLogin("CUSTOMER@TEST.LOCAL", ct))!.UserId);
        Assert.Equal(Customer, (await _repository.FindLogin("+27821234567", ct))!.UserId);
    }

    [Fact]
    public async Task Shared_contact_recovery_changes_only_the_selected_staff_account_and_sessions()
    {
        await SharedStaffContacts();
        var ct = CancellationToken.None;
        var customerSession = await _repository.CreateSession(Customer, DateTimeOffset.UtcNow.AddHours(1), ct);
        var staffSession = await _repository.CreateSession(Agent, DateTimeOffset.UtcNow.AddHours(1), ct);
        var challenge = await _repository.CreateIdentityChallenge(Agent, "Recovery", "email", "customer@test.local", "", "123456", ct);
        await _repository.MarkIdentityChallengeReady(challenge.ChallengeId, ct);
        await _repository.CompletePasswordReset(challenge.ChallengeId, "123456", "new-staff-hash", ct);
        Assert.Equal("not-used", (await _repository.FindLogin("CUSTOMER@TEST.LOCAL", ct))!.PasswordHash);
        Assert.Equal("new-staff-hash", (await _repository.FindLogin("SUPERADMIN", ct))!.PasswordHash);
        Assert.True(await _repository.IsSessionActive(customerSession, DateTimeOffset.UtcNow, ct));
        Assert.False(await _repository.IsSessionActive(staffSession, DateTimeOffset.UtcNow, ct));
    }
}
