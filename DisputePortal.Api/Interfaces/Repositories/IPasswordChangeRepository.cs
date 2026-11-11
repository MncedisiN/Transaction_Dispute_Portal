using DisputePortal.Api.Models.Domain;

namespace DisputePortal.Api.Interfaces.Repositories;

public interface IPasswordChangeRepository
{
    Task<PasswordState?> GetPasswordState(Guid user, CancellationToken ct);

    Task ChangePassword(Guid user, string expectedHash, string newHash, CancellationToken ct);
}
