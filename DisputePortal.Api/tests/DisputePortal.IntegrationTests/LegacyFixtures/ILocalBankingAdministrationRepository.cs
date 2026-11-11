using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Repository.Interfaces;

public class RegenerateBankingRequest
{
    public Guid RequestId { get; set; }
    public Guid? AccountId { get; set; }

    [Range(30, 50)]
    public int CountPerAccount { get; set; }

    [Required, MinLength(10), MaxLength(500)]
    public string Reason { get; set; } = null!;

    public RegenerateBankingRequest()
    {
    }

    public RegenerateBankingRequest(RegenerateBankingRequest source)
    {
        RequestId = source.RequestId;
        AccountId = source.AccountId;
        CountPerAccount = source.CountPerAccount;
        Reason = source.Reason;
    }
}

public class BankingRegenerationTarget
{
    public Guid AccountId { get; set; }
    public string CustomerName { get; set; } = null!;
    public string AccountType { get; set; } = null!;
    public string AccountNumber { get; set; } = null!;
    public int TransactionCount { get; set; }
}

public class BankingRegenerationResult
{
    public Guid RequestId { get; set; }
    public int AccountCount { get; set; }
    public int ArchivedCount { get; set; }
    public int CreatedCount { get; set; }
}

public interface ILocalBankingAdministrationRepository
{
    Task<IReadOnlyList<BankingRegenerationTarget>> GetRegenerationTargets(Guid actor, CancellationToken ct);

    Task<BankingRegenerationResult> RegenerateTransactions(Guid actor, RegenerateBankingRequest request, CancellationToken ct);
}
