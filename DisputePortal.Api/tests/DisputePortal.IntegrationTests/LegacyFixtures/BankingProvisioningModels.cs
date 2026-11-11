namespace DisputePortal.Api.Models.Responses;

public class BankingBatchDto
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public int Count { get; set; }
    public string State { get; set; } = null!;
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class ProvisionedBankAccountDto
{
    public Guid AccountId { get; set; }
    public string AccountType { get; set; } = "";
    public string AccountNumber { get; set; } = "";
    public int TransactionCount { get; set; }
    public BankingBatchDto? LastBatch { get; set; }
}

public class BankingProvisioningStatusDto
{
    public bool IdentityRecorded { get; set; }
    public bool ContactVerified { get; set; }
    public int AccountLimit { get; set; }
    public IReadOnlyList<ProvisionedBankAccountDto> Accounts { get; set; } = null!;
}

public class ProvisionedAccountCreatedDto
{
    public Guid AccountId { get; set; }
    public Guid BatchId { get; set; }
}
