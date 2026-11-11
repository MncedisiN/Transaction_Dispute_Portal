namespace DisputePortal.Api.Models.Configuration;

public class AssessmentDataOptions
{
    public int PollSeconds { get; set; }
    public int BatchSize { get; set; }
    public int HistoryDays { get; set; }
    public AssessmentAccountTemplate[] Accounts { get; set; } = [];

    public void Validate()
    {
        if (PollSeconds is < 1 or > 300 || BatchSize is < 1 or > 100
            || HistoryDays is < 1 or > 30 || Accounts.Length != 2
            || Accounts.Select(account => account.Type).Distinct(StringComparer.Ordinal).Count() != 2)
        {
            throw new InvalidOperationException("Configure two distinct assessment account types and bounded provisioning settings.");
        }

        foreach (var account in Accounts)
        {
            if (string.IsNullOrWhiteSpace(account.Type) || account.Type.Length > 64
                || account.Transactions.Length is < 2 or > 50
                || account.Transactions.Select(item => item.Code).Distinct(StringComparer.Ordinal).Count() != account.Transactions.Length
                || account.Transactions.Any(item => string.IsNullOrWhiteSpace(item.Code) || item.Code.Length > 64
                    || string.IsNullOrWhiteSpace(item.Type) || item.Type.Length > 64
                    || string.IsNullOrWhiteSpace(item.Description) || item.Description.Length > 500
                    || string.IsNullOrWhiteSpace(item.Merchant) || item.Merchant.Length > 120
                    || item.Amount <= 0 || decimal.Round(item.Amount, 2) != item.Amount
                    || item.Direction is not ("Credit" or "Debit")
                    || item.MinutesBeforePreparation is < 0 or > 43200
                    || item.Type == "DebitOrder" && (string.IsNullOrWhiteSpace(item.DebitOrderScheme)
                        || string.IsNullOrWhiteSpace(item.MandateStatus))))
            {
                throw new InvalidOperationException("Assessment transaction templates are incomplete or invalid.");
            }

            decimal balance = 0;
            foreach (var item in account.Transactions)
            {
                balance += item.Direction == "Credit" ? item.Amount : -item.Amount;
                if (balance < 0)
                {
                    throw new InvalidOperationException("Assessment transactions must not overdraw the opening balance.");
                }
            }
        }
    }
}

public class AssessmentAccountTemplate
{
    public string Type { get; set; } = "";
    public AssessmentTransactionTemplate[] Transactions { get; set; } = [];
}

public class AssessmentTransactionTemplate
{
    public string Code { get; set; } = "";
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
    public string Merchant { get; set; } = "";
    public decimal Amount { get; set; }
    public string Direction { get; set; } = "";
    public int MinutesBeforePreparation { get; set; }
    public bool DisputeEligible { get; set; }
    public string DebitOrderScheme { get; set; } = "";
    public string MandateStatus { get; set; } = "";
}
