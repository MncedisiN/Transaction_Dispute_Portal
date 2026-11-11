using System.Text.Json;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Ledger;
using DisputePortal.Platform.Ledger.Domain;
using Xunit;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Ledger.Interfaces;

namespace DisputePortal.Platform.Tests;

public sealed class PostingStrategyTests
{
    private static PlatformSettings Settings => JsonSerializer.Deserialize<PlatformSettings>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "seed-settings.json")),
        DomainEvent.Json)!;

    private static readonly Guid User = Guid.NewGuid(), Source = Guid.NewGuid(), Target = Guid.NewGuid();
    private static PostingPositionFactory Positions => new();

    private static IPostingPlanFactory Factory() =>
        new PostingPlanFactory([new TransferPostingStrategy(), new DepositPostingStrategy(), new WithdrawalPostingStrategy(), new RefundPostingStrategy(), new RecallPostingStrategy()]);

    [Theory]
    [InlineData("Deposit")]
    [InlineData("Withdrawal")]
    [InlineData("Transfer")]
    [InlineData("Refund")]
    [InlineData("Recall")]
    public void Every_supported_strategy_produces_balanced_entries(string kind)
    {
        var r = new PostJournalRequest
        {
            TransactionId = Guid.NewGuid(),
            UserId = User,
            Kind = kind,
            Reference = "TX-" + Guid.NewGuid().ToString("N"),
            AccountId = Source,
            RecipientAccountId = kind == "Transfer" ? Target : null,
            Amount = 100,
            Fee = kind is "Refund" or "Recall" ? 0 : 2,
            Currency = "ZAR",
            TargetAmount = 100,
            TargetCurrency = "ZAR",
            QuoteId = null,
            OriginalTransactionId = Guid.NewGuid()
        };
        var plan = Factory().CreatePostingPlan(new global::DisputePortal.Platform.Ledger.Domain.PostingContext
        {
            Request = r,
            Source = Positions.Account(Source, "ZAR"),
            Target = kind == "Transfer" ? Positions.Account(Target, "ZAR") : null,
            Settings = Settings
        });
        Assert.All(plan.Lines.GroupBy(v => v.Currency), group => Assert.Equal(0, group.Sum(v => v.Side == "D" ? v.Amount : -v.Amount)));
        Assert.Equal(plan.Lines.Count, plan.Lines.Select(v => v.Id).Distinct().Count());
    }

    [Fact]
    public void Invalid_event_envelope_cannot_contain_a_password() =>
        Assert.Throws<InvalidOperationException>(() => DomainEvent.CreateDomainEvent(
        "identity",
        EventTypes.UserRegistered,
        new { password = "must-not-be-published" }));

    [Fact]
    public void Reversal_must_match_original_account_and_amount()
    {
        var original = new PostJournalRequest
        {
            TransactionId = Guid.NewGuid(),
            UserId = User,
            Kind = "Withdrawal",
            Reference = "TX-original",
            AccountId = Source,
            RecipientAccountId = null,
            Amount = 100,
            Fee = 0,
            Currency = "ZAR",
            TargetAmount = null,
            TargetCurrency = null,
            QuoteId = null
        };
        var reversal = new global::DisputePortal.Platform.Contracts.PostJournalRequest(original)
        {
            TransactionId = Guid.NewGuid(),
            Kind = "Refund",
            OriginalTransactionId = original.TransactionId,
            Amount = 101
        };
        Assert.Throws<PlatformException>(() => ReversalPolicy.ValidateReversalRequest(reversal, original));
    }

    private sealed class PostingPositionFactory
    {
        public LedgerPosition Account(Guid id, string currency) => new global::DisputePortal.Platform.Ledger.Domain.LedgerPosition
        {
            AccountId = id,
            UserId = User,
            Currency = currency,
            CountryCode = "ZA",
            Kind = "Customer",
            Status = "Active",
            Balance = 1000,
            Reserved = 0,
            Version = 0
        };
    }
}
