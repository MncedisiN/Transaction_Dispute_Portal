namespace DisputePortal.Api.Models.Requests;

public class WithdrawDisputeRequest
{
    public int ExpectedVersion { get; set; }

    public WithdrawDisputeRequest()
    {
    }

    public WithdrawDisputeRequest(int ExpectedVersion)
    {
        this.ExpectedVersion = ExpectedVersion;
    }
}
