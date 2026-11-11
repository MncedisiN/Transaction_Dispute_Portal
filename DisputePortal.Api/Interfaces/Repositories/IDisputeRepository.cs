using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Models.Domain;

namespace DisputePortal.Api.Interfaces.Repositories;

public interface IDisputeRepository
{
    Task<CreateDisputeResult> CreateDispute(
        Guid customerId,
        CreateDisputeCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<PageResult<DisputeSummaryDto>> GetCustomerDisputes(
        Guid customerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken,
        Guid? accountId = null,
        string? status = null,
        string? search = null,
        DisputePortal.Api.Models.Internal.HistoryFilter? filter = null);

    Task<DisputeDetailDto?> GetCustomerDispute(Guid customerId, Guid disputeId, CancellationToken cancellationToken);

    Task<DashboardDto> GetDashboard(CancellationToken cancellationToken, Guid? actorId = null);

    Task<PageResult<AdminQueueItemDto>> GetAdminQueue(AdminQueueFilter filter, CancellationToken cancellationToken, Guid? actorId = null);

    Task<AdminDisputeDto?> GetAdminDispute(Guid disputeId, CancellationToken cancellationToken, Guid? actorId = null);

    Task<int> TransitionDispute(
        Guid actorId,
        Guid disputeId,
        TransitionCommand command,
        string correlationId,
        CancellationToken cancellationToken);

    Task<int> WithdrawCustomerDispute(
        Guid customerId,
        Guid disputeId,
        int expectedVersion,
        string correlationId,
        CancellationToken cancellationToken);

    Task<CreateDisputeResult> ReinstateCustomerDispute(
        Guid customerId,
        Guid disputeId,
        DisputePortal.Api.Models.Requests.ReinstateDisputeRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    Task AddNote(Guid actorId, Guid disputeId, NoteCommand command, string correlationId, CancellationToken cancellationToken);
}
