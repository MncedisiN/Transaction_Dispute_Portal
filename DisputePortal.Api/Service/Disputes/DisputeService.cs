using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Disputes;

namespace DisputePortal.Api.Service.Disputes;

public class DisputeService(IDisputeRepository repository, DisputePortal.Api.Interfaces.Repository.IDisputeInputPolicyStore policies) : IDisputeService
{
    public async Task<CreateDisputeResult> CreateCustomerDispute(
        Guid customerId,
        CreateDisputeCommand command,
        string idempotencyKey,
        CancellationToken ct)
    {
        var policy = await policies.GetDisputeInputPolicy(ct);
        var expectedReason = policy.Categories.FirstOrDefault(value => value.Code == command.Category)?.Reason;
        if (expectedReason is null)
        {
            throw new PortalValidationException("Select a valid dispute category.", "category");
        }

        if (!string.Equals(expectedReason, command.Reason, StringComparison.Ordinal))
        {
            throw new PortalValidationException("Select the reason that matches the dispute category.", "reason");
        }

        if (command.Reason.Length > policy.ReasonMaximumLength)
        {
            throw new PortalValidationException($"Reason must be no more than {policy.ReasonMaximumLength:N0} characters.", "reason");
        }

        if (command.Description.Trim().Length < policy.DescriptionMinimumLength
            || command.Description.Trim().Length > policy.DescriptionMaximumLength)
        {
            throw new PortalValidationException(
                $"Description must be between {policy.DescriptionMinimumLength:N0} and {policy.DescriptionMaximumLength:N0} characters.",
                "description");
        }

        if (idempotencyKey.Length < policy.IdempotencyKeyMinimumLength || idempotencyKey.Length > policy.IdempotencyKeyMaximumLength)
        {
            throw new PortalValidationException("A valid Idempotency-Key header is required.", "idempotencyKey");
        }

        return await repository.CreateDispute(
            customerId,
            new global::DisputePortal.Api.Models.Domain.CreateDisputeCommand(command) { Description = command.Description.Trim() },
            idempotencyKey,
            ct);
    }

    public async Task<int> Transition(Guid actorId, Guid disputeId, TransitionCommand command, string correlationId, CancellationToken ct)
    {
        var policy = await policies.GetDisputeInputPolicy(ct);
        if (command.Status.Length > policy.StatusMaximumLength || !Enum.TryParse<DisputeStatus>(command.Status, true, out var next))
        {
            throw new PortalValidationException("Select a valid dispute status.", "status");
        }

        if (command.ExpectedVersion < 1)
        {
            throw new PortalValidationException("Expected version must be positive.", "expectedVersion");
        }

        var current = await repository.GetAdminDispute(disputeId, ct, actorId) ?? throw new PortalNotFoundException();
        if (!Enum.TryParse<DisputeStatus>(current.Dispute.Status, true, out var previous)
            || !DisputeWorkflow.CanTransition(previous, next))
        {
            throw new PortalConflictException($"A dispute cannot move from {current.Dispute.Status} to {next}.");
        }

        if (command.Comment?.Length > policy.CommentMaximumLength)
        {
            throw new PortalValidationException($"Comment must be no more than {policy.CommentMaximumLength:N0} characters.", "comment");
        }

        return await repository.TransitionDispute(
            actorId,
            disputeId,
            new TransitionCommand(command.ExpectedVersion, next.ToString(), command.Comment),
            correlationId,
            ct);
    }

    public Task<int> Withdraw(Guid customerId, Guid disputeId, WithdrawDisputeCommand command, string correlationId, CancellationToken ct)
    {
        if (command.ExpectedVersion < 1)
        {
            throw new PortalValidationException("Expected version must be positive.", "expectedVersion");
        }

        return repository.WithdrawCustomerDispute(customerId, disputeId, command.ExpectedVersion, correlationId, ct);
    }

    public async Task AddNote(Guid actorId, Guid disputeId, NoteCommand command, string correlationId, CancellationToken ct)
    {
        var policy = await policies.GetDisputeInputPolicy(ct);
        if (command.Content.Trim().Length < policy.NoteMinimumLength || command.Content.Trim().Length > policy.NoteMaximumLength)
        {
            throw new PortalValidationException(
                $"Note must be between {policy.NoteMinimumLength:N0} and {policy.NoteMaximumLength:N0} characters.",
                "content");
        }

        if (command.Visibility is not ("Customer" or "Internal"))
        {
            throw new PortalValidationException("Visibility must be Customer or Internal.", "visibility");
        }

        await repository.AddNote(actorId, disputeId, new NoteCommand(command.Content.Trim(), command.Visibility), correlationId, ct);
    }
}
