using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class OpenProvisionedAccountRequest
{
    public Guid RequestId { get; set; }

    [Required, RegularExpression("^(Cheque|Savings|Credit)$")]
    public string AccountType { get; set; } = null!;

    [Range(1, 50)]
    public int TransactionCount { get; set; } = 10;
}

public class GenerateBankingTransactionsRequest
{
    public Guid RequestId { get; set; }

    [Range(1, 50)]
    public int Count { get; set; } = 10;
}
