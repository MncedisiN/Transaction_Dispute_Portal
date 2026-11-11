using DisputePortal.Api.Models.Responses;

namespace DisputePortal.Api.Interfaces.Repositories;

public interface IStaffTransactionRepository
{
    Task<PageResult<StaffTransactionDto>> Search(
        Guid actor,
        int page,
        int pageSize,
        string? search,
        string? status,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct);

    Task<StaffTransactionDetailDto> GetStaffTransactionDetails(Guid actor, Guid transaction, CancellationToken ct);
}
