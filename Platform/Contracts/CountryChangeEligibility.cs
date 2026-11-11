namespace DisputePortal.Platform.Contracts;

public class CountryChangeEligibility
{
    public bool HasNonzeroBalances { get; set; }
    public bool HasReserves { get; set; }
    public bool HasPendingRefunds { get; set; }
    public bool HasUnfinishedOperations { get; set; }
    public string? Blocker { get; set; }
    public bool Allowed => !HasNonzeroBalances && !HasReserves && !HasPendingRefunds && !HasUnfinishedOperations;
}
