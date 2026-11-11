using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;

namespace DisputePortal.Api.Interfaces.Repositories;

public interface IOperationsRepository
{
    Task SetAvailability(Guid actor, string availability, CancellationToken ct);

    Task<IReadOnlyList<CaseOperationsDto>> GetCases(Guid actor, CancellationToken ct);

    Task<CaseOperationsDto> GetCase(Guid actor, Guid id, CancellationToken ct);

    Task ClaimCase(Guid actor, Guid id, ClaimCaseRequest request, CancellationToken ct);

    Task Assign(Guid actor, Guid id, AssignmentRequest request, CancellationToken ct);

    Task Triage(Guid actor, Guid id, TriageRequest request, CancellationToken ct);

    Task UpdateCaseDeadlines(Guid actor, Guid id, UpdateCaseDeadlinesRequest request, CancellationToken ct);

    Task AssignWaitingCases(CancellationToken ct);

    Task Escalate(Guid actor, Guid dispute, EscalationRequest request, CancellationToken ct);
}
