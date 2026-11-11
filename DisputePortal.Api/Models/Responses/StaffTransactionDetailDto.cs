namespace DisputePortal.Api.Models.Responses;

public class StaffTransactionDetailDto
{
    public StaffTransactionDto Transaction { get; set; } = null!;
    public AssistedCustomerDto Customer { get; set; } = null!;
    public string? FullAccountNumber { get; set; }
    public string AccountStatus { get; set; } = string.Empty;

    public StaffTransactionDetailDto()
    {
    }

    public StaffTransactionDetailDto(
        StaffTransactionDto Transaction,
        AssistedCustomerDto Customer,
        string? FullAccountNumber,
        string AccountStatus)
    {
        this.Transaction = Transaction;
        this.Customer = Customer;
        this.FullAccountNumber = FullAccountNumber;
        this.AccountStatus = AccountStatus;
    }
}
