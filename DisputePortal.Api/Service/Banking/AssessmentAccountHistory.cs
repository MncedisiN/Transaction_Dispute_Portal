using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DisputePortal.Api.Models.Configuration;
using DisputePortal.Platform.Contracts;

namespace DisputePortal.Api.Service.Banking;

public static class AssessmentAccountHistory
{
    public static Guid StableId(Guid account, string purpose) => new(
        SHA256.HashData(Encoding.UTF8.GetBytes($"assessment:{account:D}:{purpose}"))[..16]);

    public static ClaimsPrincipal Customer(Guid user) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, user.ToString("D")), new Claim(ClaimTypes.Role, "customer")],
        "assessment-provisioning"));

    public static AssessmentAccountData Create(AccountRecord account, AssessmentAccountTemplate template,
        int historyDays, DateTimeOffset now)
    {
        if (account.Type != template.Type || account.Status != "Active"
            || account.LedgerBalance != 0 || account.ReservedBalance != 0 || account.CreatedAt > now)
        {
            throw new InvalidOperationException("Only a matching active empty account can receive assessment history.");
        }

        var captured = now < account.CreatedAt.AddDays(historyDays)
            ? now : account.CreatedAt.AddDays(historyDays).AddTicks(-10);
        var result = new AssessmentAccountData();
        var items = template.Transactions.Select(item =>
        {
            var id = StableId(account.Id, item.Code);
            var occurred = captured.AddMinutes(-item.MinutesBeforePreparation);
            if (occurred < account.CreatedAt)
            {
                occurred = account.CreatedAt;
            }

            occurred = new DateTimeOffset(occurred.UtcTicks - occurred.UtcTicks % 10, TimeSpan.Zero);
            if (item.Type == "DebitOrder")
            {
                result.DebitOrders[id] = new AssessmentDebitOrderDetails
                {
                    Scheme = item.DebitOrderScheme,
                    MandateStatus = item.MandateStatus
                };
            }

            return new PriorTransaction
            {
                Id = id,
                Reference = "AS-" + id.ToString("N"),
                OccurredAt = occurred,
                Description = item.Description,
                MerchantName = item.Merchant,
                Amount = item.Amount,
                Currency = account.Currency,
                Type = item.Type,
                Direction = item.Direction,
                Status = "Posted",
                DisputeEligible = item.DisputeEligible && item.Direction == "Debit",
                CurrentDelta = item.Direction == "Credit" ? item.Amount : -item.Amount,
                ReservedDelta = 0
            };
        }).ToArray();
        result.History = new AccountHistorySnapshot
        {
            SnapshotId = StableId(account.Id, "history"),
            ActorId = account.UserId,
            SourceSystem = "assessment",
            CapturedAt = captured,
            AccountId = account.Id,
            UserId = account.UserId,
            Number = account.Number,
            AccountType = account.Type,
            CountryCode = account.CountryCode,
            Currency = account.Currency,
            CreatedAt = account.CreatedAt,
            ExpectedBalance = items.Sum(item => item.CurrentDelta),
            ExpectedReserved = 0,
            Transactions = items
        };
        result.History.Hash = result.History.ComputeHash();
        result.History.ValidateAccountHistorySnapshot();
        return result;
    }
}
