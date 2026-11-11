using Dapper;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Service.Disputes;
using Xunit;
using DisputePortal.Api.Models.Domain;

namespace DisputePortal.IntegrationTests;

public sealed partial class DisputeWorkflowRepositoryTests
{
    private async Task GrantAssignmentSupervisor(string role)
    {
        await using var connection = await _connections.OpenConnection(default);
        await connection.ExecuteAsync(
            """
            INSERT INTO "identity"."userRoles" ("userId", "roleId")
            SELECT "u"."id", "r"."id"
            FROM "identity"."users" AS "u"
            CROSS JOIN "identity"."roles" AS "r"
            WHERE "u"."publicId" = @UserId
                AND "r"."code" = @Role
            """,
            new { UserId = OtherCustomer, Role = role });
    }

    [Theory]
    [InlineData("super_admin")]
    [InlineData("operations_manager")]
    public async Task Supervisor_self_assignment_and_status_change_work_without_staff_capacity(string role)
    {
        await GrantAssignmentSupervisor(role);
        await _repository.SetAvailability(Agent, "Busy", default);
        var created = await _repository.CreateDispute(
            Customer,
            new CreateDisputeCommand
            {
                TransactionId = Transaction,
                Category = "Unrecognized",
                Reason = "Not recognised",
                Description = "Please investigate this card purchase."
            },
            "supervisor-self-assignment",
            default);
        var before = await _repository.GetCase(OtherCustomer, created.Id, default);
        Assert.Null(before.AssignedTo);
        Assert.True(Assert.Single(before.Actions.Assignees, person => person.Id == OtherCustomer).Eligible);
        await _repository.Assign(
            OtherCustomer,
            created.Id,
            new AssignmentRequest(before.Version, OtherCustomer, "I will personally review this case."),
            default);
        var owned = await _repository.GetCase(OtherCustomer, created.Id, default);
        Assert.Equal(OtherCustomer, owned.AssignedTo);
        var version = await _repository.TransitionDispute(
            OtherCustomer,
            created.Id,
            new TransitionCommand(owned.Version, "UnderReview", "Supervisor review has started."),
            "supervisor-review",
            default);
        var reviewed = await _repository.GetCase(OtherCustomer, created.Id, default);
        Assert.Equal("UnderReview", reviewed.Status);
        Assert.Equal(version, reviewed.Version);
    }

    [Fact]
    public async Task Promoted_user_can_set_availability_when_capacity_record_was_missing()
    {
        await GrantAssignmentSupervisor("super_admin");
        await _repository.SetAvailability(OtherCustomer, "Available", default);
        var staff = await _repository.GetStaff(OtherCustomer, true, default);
        var supervisor = Assert.Single(staff, person => person.Id == OtherCustomer && person.Role == "super_admin");
        Assert.Equal("Available", supervisor.Availability);
        Assert.True(supervisor.Capacity > 0);
        await _repository.SetAvailability(OtherCustomer, "Busy", default);
        Assert.Equal(
            "Busy",
            Assert.Single(
                await _repository.GetStaff(OtherCustomer, true, default),
                person => person.Id == OtherCustomer && person.Role == "super_admin").Availability);
    }

    [Fact]
    public async Task Updating_registered_customer_to_staff_creates_capacity_record()
    {
        await GrantAssignmentSupervisor("super_admin");
        await _repository.UpdateUser(OtherCustomer, Customer, new UpdateUserRequest("fraud_agent", "Active", 7), default);
        var staff = Assert.Single(await _repository.GetStaff(OtherCustomer, true, default), person => person.Id == Customer);
        Assert.Equal("fraud_agent", staff.Role);
        Assert.Equal(7, staff.Capacity);
    }
}
