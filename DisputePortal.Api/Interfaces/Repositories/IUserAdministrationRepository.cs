using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;

namespace DisputePortal.Api.Interfaces.Repositories;

public interface IUserAdministrationRepository
{
    Task<IReadOnlyList<StaffUserDto>> GetStaff(Guid actor, bool administration, CancellationToken ct);

    Task<Guid> CreateUser(Guid actor, SaveUserRequest request, string passwordHash, CancellationToken ct);

    Task UpdateUser(Guid actor, Guid id, UpdateUserRequest request, CancellationToken ct);
}
