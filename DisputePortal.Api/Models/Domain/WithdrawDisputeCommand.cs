namespace DisputePortal.Api.Models.Domain;

public class WithdrawDisputeCommand
{
    public int ExpectedVersion { get; set; }

    public WithdrawDisputeCommand(int expectedVersion)
    {
        ExpectedVersion = expectedVersion;
    }
}
