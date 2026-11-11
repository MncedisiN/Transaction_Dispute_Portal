namespace DisputePortal.Api.Models.Responses;

public class AccountNumberDto
{
    public string AccountNumber { get; set; } = string.Empty;

    public AccountNumberDto()
    {
    }

    public AccountNumberDto(string AccountNumber)
    {
        this.AccountNumber = AccountNumber;
    }
}
