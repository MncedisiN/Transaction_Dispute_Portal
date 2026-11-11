using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;

namespace DisputePortal.Api.Interfaces.Repositories;

public interface ISupportRepository
{
    Task<IReadOnlyList<SupportConcernDto>> GetConcerns(CancellationToken ct);

    Task<SupportThreadDto> CloseConversation(Guid actor, Guid thread, CloseSupportRequest request, CancellationToken ct);

    Task<SupportThreadDto> AssignConversation(Guid actor, Guid thread, AssignmentRequest request, CancellationToken ct);

    Task<SupportThreadDto> EscalateConversation(Guid actor, Guid thread, EscalationRequest request, CancellationToken ct);

    Task AssignWaitingSupportConversations(CancellationToken ct);

    Task<PageResult<SupportCaseOptionDto>> GetRelatedCases(Guid actor, int page, int pageSize, string? reference, CancellationToken ct);

    Task<PageResult<SupportThreadDto>> GetThreads(
        Guid actor,
        CancellationToken ct,
        Guid? disputeId = null,
        string? search = null,
        Guid? transactionId = null,
        DisputePortal.Api.Models.Internal.HistoryFilter? filter = null,
        Guid? relatedDisputeId = null,
        int page = 1,
        int pageSize = 100,
        bool assignedOnly = false,
        bool ownOnly = false);

    Task<IReadOnlyList<SupportMessageDto>> GetMessages(
        Guid actor,
        Guid thread,
        CancellationToken ct,
        long? afterSequence = null,
        long? beforeSequence = null);

    Task<Guid> SendMessage(Guid actor, SupportSendRequest request, CancellationToken ct);

    Task<SupportMessagePageDto> GetMessagePage(
        Guid actor,
        Guid thread,
        CancellationToken ct,
        long? afterSequence = null,
        long? beforeSequence = null);
}
