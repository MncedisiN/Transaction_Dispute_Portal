namespace DisputePortal.Api.Models.Domain;
// Historical fixture data only; production account eligibility comes from saved configuration.
internal static class CustomerAccountPolicy
{
    public const int MaximumAccounts = 2;
    public static IReadOnlyList<string> AccountTypes { get; } = Array.AsReadOnly(new[] { "Cheque", "Savings" });

    public static bool CanOpen(string accountType) => AccountTypes.Contains(accountType, StringComparer.Ordinal);
}
