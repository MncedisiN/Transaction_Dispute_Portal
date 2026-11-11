using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Models.Domain;

namespace DisputePortal.Api.Service.Disputes;

public static class CaseActionPresenter
{
    public static CaseActionsDto Describe(
        CaseOperationsDto item,
        Guid actor,
        IReadOnlyCollection<string> roles,
        IReadOnlySet<string> permissions,
        IReadOnlyList<StaffUserDto> staff,
        bool reopeningBlocked)
    {
        var known = Enum.TryParse<DisputeStatus>(item.Status, out var status);
        var closed = !known || !DisputeWorkflow.IsActive(status);
        var supervisor = roles.Any(CaseServicePolicy.IsSupervisor);
        var qualified = roles.Any(role => CaseServicePolicy.CanWork(role, item.QueueType));
        var manage = permissions.Contains("disputes.manage") && (supervisor || item.AssignedTo == actor && qualified);
        var actions = new CaseActionsDto
        {
            IsClosed = closed,
            CanManage = manage,
            CanStartConversation = !closed && (actor == item.CustomerId || manage),
            CanAssign = !closed && supervisor && permissions.Contains("operations.manage"),
            CanEscalate = !closed && manage,
            CanAddNote = !closed && manage,
            CanWithdraw = known && actor == item.CustomerId && DisputeWorkflow.CanCustomerWithdraw(status),
            CanTakeOwnership = !closed && supervisor && permissions.Contains("disputes.manage") && item.AssignedTo != actor,
            AllowedTransitions = known && manage && (!closed || roles.Contains("super_admin") && !reopeningBlocked) ? Enum.GetValues<DisputeStatus>().Where(next => DisputeWorkflow.CanTransition(
            status,
            next)).Select(next => next.ToString()).ToArray() : []
        };
        if (permissions.Contains("disputes.manage"))
        {
            actions.Assignees = staff.GroupBy(person => person.Id).Select(group => group.FirstOrDefault(person => CaseServicePolicy.CanBeAssigned(
                person.Role,
                item.QueueType,
                item.EscalationLevel,
                person.Id == actor && supervisor))).Where(person => person is not null).Select(person =>
            {
                var p = person!;
                var reason = p.Status != "Active" ? "Account " + p.Status.ToLowerInvariant() : p.Id == actor && supervisor ? string.Empty : p.Availability != "Available" ? p.Availability : p.ActiveCases >= p.Capacity && p.Id != item.AssignedTo ? "At capacity" : string.Empty;
                return new AssigneeOptionDto
                {
                    Id = p.Id,
                    DisplayName = p.DisplayName,
                    Role = p.Role,
                    ActiveCases = p.ActiveCases,
                    Capacity = p.Capacity,
                    Eligible = !closed && reason.Length == 0,
                    UnavailableReason = reason
                };
            }).ToArray();
        }

        actions.Claim = DescribeClaim(item, actor, permissions.Contains("disputes.manage"), supervisor, qualified, staff, closed);
        return actions;
    }

    private static ClaimActionDto DescribeClaim(
        CaseOperationsDto item,
        Guid actor,
        bool hasPermission,
        bool supervisor,
        bool qualified,
        IReadOnlyList<StaffUserDto> staff,
        bool closed)
    {
        ClaimActionDto Locked(string reason) => new()
        {
            Reason = reason
        };
        if (!hasPermission)
        {
            return Locked("Your role has read-only access.");
        }

        if (closed)
        {
            return new()
            {
                State = "closed",
                Label = "Case Closed"
            };
        }

        if (!supervisor && !qualified)
        {
            return Locked("A qualified specialist must review this case.");
        }

        if (item.AssignedTo == actor)
        {
            return new()
            {
                State = "owned",
                Label = "Assigned To You"
            };
        }

        if (item.AssignedTo.HasValue)
        {
            return new()
            {
                State = "assigned",
                Label = "Assigned To " + (item.AssignedName ?? "a specialist"),
                Reason = supervisor ? "Reassign from the case workflow with a handover reason." : "A supervisor can arrange a handover."
            };
        }

        if (!supervisor)
        {
            var person = staff.FirstOrDefault(value => value.Id == actor);
            if (person is null || person.Status != "Active")
            {
                return Locked("An active staff account is required.");
            }

            if (person.ActiveCases >= person.Capacity)
            {
                return Locked("Your case capacity is full.");
            }
        }

        return new()
        {
            Allowed = true,
            State = "ready",
            Label = "Take This Case"
        };
    }
}
