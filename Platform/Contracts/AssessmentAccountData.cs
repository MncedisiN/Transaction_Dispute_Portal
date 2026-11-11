namespace DisputePortal.Platform.Contracts;

public class AssessmentAccountData
{
    public AccountHistorySnapshot History { get; set; } = new();
    public Dictionary<Guid, AssessmentDebitOrderDetails> DebitOrders { get; set; } = [];
}

public class AssessmentDebitOrderDetails
{
    public string Scheme { get; set; } = "";
    public string MandateStatus { get; set; } = "";
}
