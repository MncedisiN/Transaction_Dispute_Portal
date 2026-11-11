using System.Security.Cryptography;
using System.Text;

namespace DisputePortal.Api.Models.Domain;

public class BankingTransaction
{
    public Guid Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string Description { get; set; } = null!;
    public string Merchant { get; set; } = null!;
    public decimal Amount { get; set; }
    public string Type { get; set; } = null!;
    public string Direction { get; set; } = null!;
    public string Status { get; set; } = null!;
    public bool Eligible { get; set; }
    public string? Scheme { get; set; }
    public string? Mandate { get; set; }

    public BankingTransaction()
    {
    }

    public BankingTransaction(BankingTransaction source)
    {
        Id = source.Id;
        At = source.At;
        Description = source.Description;
        Merchant = source.Merchant;
        Amount = source.Amount;
        Type = source.Type;
        Direction = source.Direction;
        Status = source.Status;
        Eligible = source.Eligible;
        Scheme = source.Scheme;
        Mandate = source.Mandate;
    }
}

// Fictional counterparties and synthetic amounts only. This never calls a bank or initiates a payment.
public static class BankingTransactionFactory
{
    private static readonly Scenario[] Scenarios = [new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "CardPurchase",
        Merchant = "Metro Grocer",
        Description = "Grocery card purchase",
        Amount = 79.90m,
        Days = 0
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "DebitOrder",
        Merchant = "Cover Insurance",
        Description = "Insurance debit order",
        Amount = 549.00m,
        Days = 3,
        Status = "Posted",
        Scheme = "DebiCheck",
        Mandate = "Valid"
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "Transfer",
        Merchant = "Household payment",
        Description = "Immediate beneficiary payment",
        Amount = 420.00m,
        Days = 6
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "CashWithdrawal",
        Merchant = "Neighbourhood ATM",
        Description = "ATM cash withdrawal",
        Amount = 60.00m,
        Days = 12
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "Deposit",
        Merchant = "Payroll Services",
        Description = "Salary credit",
        Amount = 18500.00m,
        Days = 28
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "Fee",
        Merchant = "Account services",
        Description = "Monthly account service fee",
        Amount = 7.50m,
        Days = 15
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "Other",
        Merchant = "Prepaid electricity",
        Description = "Prepaid electricity purchase",
        Amount = 161.00m,
        Days = 10
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "CardPurchase",
        Merchant = "Metro Grocer",
        Description = "Repeated grocery card purchase",
        Amount = 79.90m,
        Days = 0
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "CardPurchase",
        Merchant = "City Pharmacy",
        Description = "Pharmacy card purchase",
        Amount = 90.00m,
        Days = 18
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "Other",
        Merchant = "Mobile data",
        Description = "Prepaid airtime and data",
        Amount = 57.00m,
        Days = 5,
        Status = "Pending"
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "Deposit",
        Merchant = "Cash deposit",
        Description = "Branch cash deposit",
        Amount = 2400.00m,
        Days = 32
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "DebitOrder",
        Merchant = "Broadband Services",
        Description = "Older EFT debit order",
        Amount = 399.00m,
        Days = 68,
        Status = "Posted",
        Scheme = "EFT",
        Mandate = "Unknown"
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "DebitOrder",
        Merchant = "Home Cover",
        Description = "Disputed debit order mandate",
        Amount = 250.01m,
        Days = 8,
        Status = "Posted",
        Scheme = "RM",
        Mandate = "Disputed"
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "Transfer",
        Merchant = "Family payment",
        Description = "Payment to family",
        Amount = 500.00m,
        Days = 45
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "Transfer",
        Merchant = "Savings transfer",
        Description = "Transfer between accounts",
        Amount = 1800.00m,
        Days = 57
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "CardPurchase",
        Merchant = "Home Supplies",
        Description = "Household card purchase",
        Amount = 362.00m,
        Days = 76
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "CardPurchase",
        Merchant = "Neighbourhood Cafe",
        Description = "Restaurant card purchase",
        Amount = 168.00m,
        Days = 90,
        Status = "Reversed"
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "Deposit",
        Merchant = "Incoming payment",
        Description = "EFT credit received",
        Amount = 1000.00m,
        Days = 20
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "CardPurchase",
        Merchant = "Stationery Market",
        Description = "Card payment at refund threshold",
        Amount = 250.00m,
        Days = 0
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "CardPurchase",
        Merchant = "Stationery Market",
        Description = "Repeated card payment at refund threshold",
        Amount = 250.00m,
        Days = 0
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "CardPurchase",
        Merchant = "Homeware Store",
        Description = "Card payment above refund threshold",
        Amount = 250.01m,
        Days = 0
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "CardPurchase",
        Merchant = "Homeware Store",
        Description = "Repeated card payment above refund threshold",
        Amount = 250.01m,
        Days = 0
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "CardPurchase",
        Merchant = "City Fuel",
        Description = "Card payment below refund threshold",
        Amount = 249.99m,
        Days = 0
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "CardPurchase",
        Merchant = "City Fuel",
        Description = "Repeated card payment below refund threshold",
        Amount = 249.99m,
        Days = 0
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "CardPurchase",
        Merchant = "Online Books",
        Description = "Older online card purchase",
        Amount = 59.00m,
        Days = 4
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "CardPurchase",
        Merchant = "Online Books",
        Description = "Repeated older online card purchase",
        Amount = 59.00m,
        Days = 4
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "DebitOrder",
        Merchant = "Fitness Studio",
        Description = "New debit order with unknown mandate",
        Amount = 199.43m,
        Days = 1,
        Status = "Posted",
        Scheme = "EFT",
        Mandate = "Unknown"
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "Fee",
        Merchant = "Payment services",
        Description = "Immediate payment fee",
        Amount = 1.00m,
        Days = 7
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "Deposit",
        Merchant = "Interest credit",
        Description = "Account interest received",
        Amount = 1.04m,
        Days = 30
    }, new global::DisputePortal.Api.Models.Domain.BankingTransactionFactory.Scenario
    {
        Type = "Transfer",
        Merchant = "Incoming transfer",
        Description = "Transfer credit received",
        Amount = 300.00m,
        Days = 2
    }

    ];

    public static IReadOnlyList<BankingTransaction> Create(
        Guid batch,
        int count,
        DateTimeOffset now,
        decimal refundMaximumAmount = 250m,
        int refundMaximumAgeHours = 48)
    {
        if (batch == Guid.Empty
            || count is < 1 or > 50
            || !DisputePolicyLimits.ValidMoney(refundMaximumAmount)
            || refundMaximumAgeHours is < 1 or > DisputePolicyLimits.MaximumHours)
        {
            throw new ArgumentException("Use a batch reference, between 1 and 50 transactions, and valid refund policy boundaries.");
        }

        var utcNow = now.ToUniversalTime();
        var backdate = TimeSpan.FromDays(Scenarios.Take(Math.Min(count, Scenarios.Length)).Max(x => x.Days)) + TimeSpan.FromMinutes(30 + (count - 1) * 13);
        if (count >= 26)
        {
            var agedPair = TimeSpan.FromHours(refundMaximumAgeHours + 12) + TimeSpan.FromMinutes(25);
            if (agedPair > backdate)
            {
                backdate = agedPair;
            }
        }

        // Reserve the extra day used by the persisted opening credit. Refuse impossible
        // timestamps explicitly instead of clamping an aged policy example into eligibility.
        if (utcNow.UtcTicks < (backdate + TimeSpan.FromDays(1)).Ticks)
        {
            throw new ArgumentOutOfRangeException(
                nameof(now),
                "The generation time cannot represent the requested history and opening credit.");
        }

        var items = new List<BankingTransaction>(count);
        for (var i = 0; i < count; i++)
        {
            var scenario = Scenarios[i % Scenarios.Length];
            var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(batch.ToString("N") + ":" + i))[..16]);
            var credit = scenario.Type == "Deposit" || i % Scenarios.Length == 29;
            var at = utcNow.AddDays(-scenario.Days).AddMinutes(-30 - i * 13);
            var amount = (i % Scenarios.Length) switch
            {
                18 or 19 => refundMaximumAmount,
                20 or 21 => refundMaximumAmount + 0.01m,
                22 or 23 => Math.Max(0.01m, refundMaximumAmount - 0.01m),
                _ => scenario.Amount
            };
            items.Add(new global::DisputePortal.Api.Models.Domain.BankingTransaction
            {
                Id = id,
                At = at,
                Description = scenario.Description,
                Merchant = scenario.Merchant,
                Amount = amount,
                Type = scenario.Type,
                Direction = credit ? "Credit" : "Debit",
                Status = scenario.Status,
                Eligible = !credit && scenario.Status == "Posted",
                Scheme = scenario.Scheme,
                Mandate = scenario.Mandate
            });
        }

        foreach (var (original, duplicate) in ConfirmedDuplicatePairs(count))
        {
            var at = original switch
            {
                0 => utcNow.AddMinutes(-25),
                18 => utcNow.AddMinutes(-35),
                20 => utcNow.AddMinutes(-45),
                22 => utcNow.AddMinutes(-50),
                _ => utcNow.AddHours(-refundMaximumAgeHours - 12).AddMinutes(-25)
            };
            items[original] = new global::DisputePortal.Api.Models.Domain.BankingTransaction(items[original])
            {
                At = at
            };
            items[duplicate] = new global::DisputePortal.Api.Models.Domain.BankingTransaction(items[duplicate])
            {
                At = at.AddMinutes(2)
            };
        }

        return items;
    }

    public static IReadOnlyList<(int Original, int Duplicate)> ConfirmedDuplicatePairs(int count) => new (int, int)[]
    {
        (0, 7),
        (18, 19),
        (20, 21),
        (22, 23),
        (24, 25)
    }.Where(pair => pair.Item2 < count).ToArray();

    public static decimal OpeningCredit(IReadOnlyList<BankingTransaction> items, decimal retainedCurrent = 0, decimal retainedReserved = 0) =>
        Math.Max(
        25000m,
        items.Where(x => x.Direction == "Debit").Sum(x => x.Amount) + 2500m + Math.Max(0, -retainedCurrent) + Math.Max(0, retainedReserved));

    private class Scenario
    {
        public string Type { get; set; } = null!;
        public string Merchant { get; set; } = null!;
        public string Description { get; set; } = null!;
        public decimal Amount { get; set; }
        public int Days { get; set; }
        public string Status { get; set; } = "Posted";
        public string? Scheme { get; set; } = null;
        public string? Mandate { get; set; } = null;
    }
}
