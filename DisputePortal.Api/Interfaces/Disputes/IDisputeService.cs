using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Models.Domain;

namespace DisputePortal.Api.Interfaces.Disputes;

public interface IDisputeService
{
    Task<CreateDisputeResult> CreateCustomerDispute(
        Guid customerId,
        CreateDisputeCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<int> Transition(Guid actorId, Guid disputeId, TransitionCommand command, string correlationId, CancellationToken cancellationToken);

    Task<int> Withdraw(
        Guid customerId,
        Guid disputeId,
        WithdrawDisputeCommand command,
        string correlationId,
        CancellationToken cancellationToken);

    Task AddNote(Guid actorId, Guid disputeId, NoteCommand command, string correlationId, CancellationToken cancellationToken);
}
