using DisputePortal.Api.Models.Responses;
using Xunit;
using DisputePortal.Api.Service.Disputes;

namespace DisputePortal.UnitTests;

public class CaseActionPresenterTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly HashSet<string> Permissions = ["disputes.manage", "operations.manage"];

    private static CaseOperationsDto Case(string status = "Submitted") => new()
    {
        Id = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        Status = status,
        QueueType = "Fraud",
        AssignedTo = Actor
    };

    [Fact]
    public void All_roles_are_considered_and_transitions_follow_the_existing_workflow()
    {
        var item = Case();
        var result = CaseActionPresenter.Describe(item, Actor, ["customer", "fraud_agent"], Permissions, [], false);
        Assert.True(result.CanManage);
        Assert.Equal(new[] { "UnderReview", "Withdrawn" }, result.AllowedTransitions);
        Assert.False(result.CanAssign);
    }

    [Theory]
    [InlineData("Resolved")]
    [InlineData("Declined")]
    [InlineData("Withdrawn")]
    public void Closed_cases_have_no_assignment_or_withdrawal_actions(string status)
    {
        var item = Case(status);
        var actions = CaseActionPresenter.Describe(item, item.CustomerId, ["super_admin"], Permissions, [], false);
        Assert.True(actions.IsClosed);
        Assert.False(actions.CanAssign);
        Assert.False(actions.CanWithdraw);
        Assert.False(actions.Claim.Allowed);
        Assert.Equal(new[] { "UnderReview" }, actions.AllowedTransitions);
        Assert.Empty(CaseActionPresenter.Describe(item, Actor, ["super_admin"], Permissions, [], true).AllowedTransitions);
    }

    [Fact]
    public void Unknown_status_and_missing_permission_fail_closed()
    {
        Assert.Empty(CaseActionPresenter.Describe(Case("FutureStatus"), Actor, ["super_admin"], Permissions, [], false).AllowedTransitions);
        Assert.False(CaseActionPresenter.Describe(Case(), Actor, ["super_admin"], new HashSet<string>(), [], false).CanManage);
    }

    [Fact]
    public void Assignees_use_all_roles_and_preserve_capacity_and_availability_rules()
    {
        var candidate = Guid.NewGuid();
        var staff = new[]
        {
            new StaffUserDto
            {
                Id = candidate,
                Role = "customer",
                Status = "Active",
                Availability = "Available",
                Capacity = 5
            },
            new StaffUserDto
            {
                Id = candidate,
                Role = "fraud_agent",
                Status = "Active",
                Availability = "Available",
                Capacity = 5
            },
            new StaffUserDto
            {
                Id = Guid.NewGuid(),
                Role = "fraud_agent",
                Status = "Active",
                Availability = "Busy",
                Capacity = 5
            },
            new StaffUserDto
            {
                Id = Guid.NewGuid(),
                Role = "fraud_agent",
                Status = "Active",
                Availability = "Available",
                Capacity = 2,
                ActiveCases = 2
            }
        };
        var options = CaseActionPresenter.Describe(Case(), Actor, ["super_admin"], Permissions, staff, false).Assignees;
        Assert.Equal(3, options.Length);
        Assert.True(Assert.Single(options, option => option.Id == candidate).Eligible);
        Assert.False(options[1].Eligible);
        Assert.Equal("At capacity", options[2].UnavailableReason);
    }

    [Theory]
    [InlineData("super_admin")]
    [InlineData("operations_manager")]
    public void Active_supervisor_can_assign_self_without_a_capacity_row(string role)
    {
        var item = Case();
        item.AssignedTo = null;
        var staff = new[]
        {
            new StaffUserDto
            {
                Id = Actor,
                Role = "customer",
                Status = "Active",
                Availability = "Offline"
            },
            new StaffUserDto
            {
                Id = Actor,
                Role = role,
                Status = "Active",
                Availability = "Offline"
            }
        };
        var actions = CaseActionPresenter.Describe(item, Actor, ["customer", role], Permissions, staff, false);
        Assert.True(actions.Claim.Allowed);
        Assert.True(Assert.Single(actions.Assignees).Eligible);
        Assert.Contains("UnderReview", actions.AllowedTransitions);
    }

    [Fact]
    public void Supervisor_override_does_not_make_other_unavailable_staff_eligible()
    {
        var other = Guid.NewGuid();
        var staff = new[]
        {
            new StaffUserDto
            {
            Id = other,
            Role = "super_admin",
            Status = "Active",
            Availability = "Offline"
        }
        };
        var item = Case();
        item.EscalationLevel = 1;
        var actions = CaseActionPresenter.Describe(item, Actor, ["super_admin"], Permissions, staff, false);
        Assert.False(Assert.Single(actions.Assignees).Eligible);
    }
}
