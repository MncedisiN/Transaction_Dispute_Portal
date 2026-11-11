using System.Text.Json;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Ledger;
using DisputePortal.Platform.Ledger.Domain;
using Xunit;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Ledger.Interfaces;

namespace DisputePortal.Platform.Tests;

public sealed class LedgerCountryPolicyTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Source = Guid.NewGuid();
    private static readonly Guid Target = Guid.NewGuid();
    private static PlatformSettings Settings => JsonSerializer.Deserialize<PlatformSettings>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "seed-settings.json")),
        DomainEvent.Json)!;

    private static LedgerPosition Account(Guid id, string currency = "ZAR", string country = "ZA", string status = "Active") =>
        new global::DisputePortal.Platform.Ledger.Domain.LedgerPosition
    {
        AccountId = id,
        UserId = User,
        Currency = currency,
        CountryCode = country,
        Kind = "Customer",
        Status = status,
        Balance = 1000,
        Reserved = 0,
        Version = 0
    };

    private static PostJournalRequest Transfer(string currency = "ZAR", string? targetCurrency = null, Guid? quote = null) =>
        new global::DisputePortal.Platform.Contracts.PostJournalRequest
    {
        TransactionId = Guid.NewGuid(),
        UserId = User,
        Kind = "Transfer",
        Reference = "TX-" + Guid.NewGuid().ToString("N"),
        AccountId = Source,
        RecipientAccountId = Target,
        Amount = 100,
        Fee = 0,
        Currency = currency,
        TargetAmount = 100,
        TargetCurrency = targetCurrency ?? currency,
        QuoteId = quote
    };

    private static IPostingPlanFactory Factory() =>
        new PostingPlanFactory([new TransferPostingStrategy(), new DepositPostingStrategy(), new WithdrawalPostingStrategy(), new RefundPostingStrategy(), new RecallPostingStrategy()]);

    [Fact]
    public void Transfer_uses_the_saved_country_default_without_conversion()
    {
        var request = Transfer();
        var plan = Factory().CreatePostingPlan(new global::DisputePortal.Platform.Ledger.Domain.PostingContext
        {
            Request = request,
            Source = Account(Source),
            Target = Account(Target),
            Settings = Settings
        });
        Assert.True(plan.RequiresReservation);
        Assert.Equal(2, plan.Lines.Count);
        Assert.All(plan.Lines, line => Assert.Equal("ZAR", line.Currency));
        Assert.Equal(100, plan.Lines.Single(line => line.AccountId == Target).Amount);
        Assert.Empty(plan.ClearingAccounts);
        Assert.Same(request, plan.Request);
    }

    [Fact]
    public void Previously_supported_currency_does_not_authorize_a_new_transfer()
    {
        var request = Transfer("USD");
        var error = Assert.Throws<PlatformException>(() => Factory().CreatePostingPlan(new global::DisputePortal.Platform.Ledger.Domain.PostingContext
        {
            Request = request,
            Source = Account(Source, "USD"),
            Target = Account(Target, "USD"),
            Settings = Settings
        }));
        Assert.Equal("country_currency_unavailable", error.Code);
    }

    [Fact]
    public void Matching_currency_in_another_jurisdiction_is_not_current()
    {
        var error = Assert.Throws<PlatformException>(() => Factory().CreatePostingPlan(new global::DisputePortal.Platform.Ledger.Domain.PostingContext
        {
            Request = Transfer(),
            Source = Account(Source, country: "ZW"),
            Target = Account(Target),
            Settings = Settings
        }));
        Assert.Equal("country_currency_unavailable", error.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Different_currency_transfer_is_rejected_with_or_without_an_old_quote(bool hasQuote)
    {
        var request = Transfer(targetCurrency: "USD", quote: hasQuote ? Guid.NewGuid() : null);
        var error = Assert.Throws<PlatformException>(() => Factory().CreatePostingPlan(new global::DisputePortal.Platform.Ledger.Domain.PostingContext
        {
            Request = request,
            Source = Account(Source),
            Target = Account(Target, "USD"),
            Settings = Settings
        }));
        Assert.Equal("cross_currency_transfer_unsupported", error.Code);
    }

    [Fact]
    public void A_stale_conversion_identifier_cannot_authorize_a_new_same_currency_posting()
    {
        var error = Assert.Throws<PlatformException>(() => Factory().CreatePostingPlan(new global::DisputePortal.Platform.Ledger.Domain.PostingContext
        {
            Request = Transfer(quote: Guid.NewGuid()),
            Source = Account(Source),
            Target = Account(Target),
            Settings = Settings
        }));
        Assert.Equal("cross_currency_transfer_unsupported", error.Code);
    }

    [Fact]
    public void Zimbabwe_uses_its_saved_ZiG_metadata_when_it_is_the_operating_country()
    {
        var settings = Settings;
        settings = new global::DisputePortal.Platform.Contracts.PlatformSettings(settings)
        {
            OperatingCountry = "ZW",
            Currencies = settings.Currencies.Any(currency => currency.Code == "ZWG") ? settings.Currencies : [..settings.Currencies, new CurrencyDefinition
            {
                Code = "ZWG",
                Name = "ZiG",
                Symbol = "ZiG",
                Decimals = 2
            }

            ],
            Countries = settings.Countries.Select(country => country.Code == "ZW" ? new global::DisputePortal.Platform.Contracts.CountryDefinition(country)
            {
                DefaultCurrency = "ZWG",
                Currencies = ["ZWG"]
            } : country).ToArray()
        };
        var plan = Factory().CreatePostingPlan(new global::DisputePortal.Platform.Ledger.Domain.PostingContext
        {
            Request = Transfer("ZWG"),
            Source = Account(Source, "ZWG", "ZW"),
            Target = Account(Target, "ZWG", "ZW"),
            Settings = settings
        });
        Assert.All(plan.Lines, line => Assert.Equal("ZWG", line.Currency));
        Assert.Equal(0, plan.Lines.Sum(line => line.Side == "D" ? line.Amount : -line.Amount));
    }

    [Theory]
    [InlineData("Deposit")]
    [InlineData("Withdrawal")]
    public void Ordinary_external_postings_also_reject_a_legacy_currency(string kind)
    {
        var request = new global::DisputePortal.Platform.Contracts.PostJournalRequest(Transfer("USD"))
        {
            Kind = kind,
            RecipientAccountId = null
        };
        var error = Assert.Throws<PlatformException>(() => Factory().CreatePostingPlan(new global::DisputePortal.Platform.Ledger.Domain.PostingContext
        {
            Request = request,
            Source = Account(Source, "USD"),
            Target = null,
            Settings = Settings
        }));
        Assert.Equal("country_currency_unavailable", error.Code);
    }

    [Fact]
    public void A_blocked_account_cannot_send_an_ordinary_transfer()
    {
        var error = Assert.Throws<PlatformException>(() => Factory().CreatePostingPlan(new global::DisputePortal.Platform.Ledger.Domain.PostingContext
        {
            Request = Transfer(),
            Source = Account(Source, status: "Blocked"),
            Target = Account(Target),
            Settings = Settings
        }));
        Assert.Equal("account_unavailable", error.Code);
    }

    [Theory]
    [InlineData("Refund")]
    [InlineData("Recall")]
    public void Historical_settlement_retains_the_original_currency_and_amount_on_a_blocked_account(string kind)
    {
        var request = new PostJournalRequest
        {
            TransactionId = Guid.NewGuid(),
            UserId = User,
            Kind = kind,
            Reference = "HISTORICAL-" + Guid.NewGuid().ToString("N"),
            AccountId = Source,
            RecipientAccountId = null,
            Amount = 123.45m,
            Fee = 0,
            Currency = "USD",
            TargetAmount = null,
            TargetCurrency = null,
            QuoteId = null,
            OriginalTransactionId = Guid.NewGuid(),
            SettingsVersion = 999
        };
        var plan = Factory().CreatePostingPlan(new global::DisputePortal.Platform.Ledger.Domain.PostingContext
        {
            Request = request,
            Source = Account(Source, "USD", status: "Blocked"),
            Target = null,
            Settings = Settings
        });
        Assert.False(plan.RequiresReservation);
        Assert.Same(request, plan.Request);
        Assert.All(
            plan.Lines,
            line =>
        {
            Assert.Equal("USD", line.Currency);
            Assert.Equal(123.45m, line.Amount);
        });
        Assert.Equal(0, plan.Lines.Sum(line => line.Side == "D" ? line.Amount : -line.Amount));
    }

    [Theory]
    [InlineData("Closed")]
    [InlineData("PendingLedger")]
    public void Historical_settlement_does_not_reopen_a_closed_or_pending_account(string status)
    {
        var request = new PostJournalRequest
        {
            TransactionId = Guid.NewGuid(),
            UserId = User,
            Kind = "Refund",
            Reference = "HISTORICAL-REFUND",
            AccountId = Source,
            RecipientAccountId = null,
            Amount = 10,
            Fee = 0,
            Currency = "USD",
            TargetAmount = null,
            TargetCurrency = null,
            QuoteId = null,
            OriginalTransactionId = Guid.NewGuid()
        };
        Assert.Throws<PlatformException>(() => Factory().CreatePostingPlan(new global::DisputePortal.Platform.Ledger.Domain.PostingContext
        {
            Request = request,
            Source = Account(Source, "USD", status: status),
            Target = null,
            Settings = Settings
        }));
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Account")]
    [InlineData("Currency")]
    [InlineData("Amount")]
    [InlineData("OriginalId")]
    public void Historical_refund_must_match_the_exact_posted_original(string mismatch)
    {
        var original = new PostJournalRequest
        {
            TransactionId = Guid.NewGuid(),
            UserId = User,
            Kind = "ExternalDebit",
            Reference = "ORIGINAL-DEBIT",
            AccountId = Source,
            RecipientAccountId = null,
            Amount = 10,
            Fee = 0,
            Currency = "USD",
            TargetAmount = null,
            TargetCurrency = null,
            QuoteId = null
        };
        var request = new global::DisputePortal.Platform.Contracts.PostJournalRequest(original)
        {
            TransactionId = Guid.NewGuid(),
            Kind = "Refund",
            OriginalTransactionId = original.TransactionId
        };
        request = mismatch switch
        {
            "Owner" => new global::DisputePortal.Platform.Contracts.PostJournalRequest(request)
            {
                UserId = Guid.NewGuid()
            },
            "Account" => new global::DisputePortal.Platform.Contracts.PostJournalRequest(request)
            {
                AccountId = Guid.NewGuid()
            },
            "Currency" => new global::DisputePortal.Platform.Contracts.PostJournalRequest(request)
            {
                Currency = "ZAR"
            },
            "Amount" => new global::DisputePortal.Platform.Contracts.PostJournalRequest(request)
            {
                Amount = 11
            },
            _ => new global::DisputePortal.Platform.Contracts.PostJournalRequest(request)
            {
                OriginalTransactionId = Guid.NewGuid()
            }
        };
        var error = Assert.Throws<PlatformException>(() => ReversalPolicy.ValidateReversalRequest(request, original));
        Assert.Equal("reversal_mismatch", error.Code);
    }

    [Fact]
    public async Task Posted_historical_replay_does_not_read_or_reinterpret_current_policy()
    {
        var result = new JournalResult(Guid.NewGuid(), Guid.NewGuid(), "POSTED-HISTORICAL", [], true);
        var request = Transfer("USD", "ZAR", Guid.NewGuid());
        var service = new LedgerCommandService(new NoWrites(), new PostedReads(result), new NoConfiguration(), new NoPlans());
        Assert.Same(result, await service.Post(request, default));
    }

    [Fact]
    public void A_new_transfer_requires_its_matching_unfinished_country_operation()
    {
        var operation = Operation();
        LedgerOperationPolicy.Require(operation, operation.OperationId, Account(Source), false);
    }

    [Theory]
    [InlineData("OperationId")]
    [InlineData("Settled")]
    [InlineData("SourceKind")]
    [InlineData("Version")]
    [InlineData("Country")]
    [InlineData("Currency")]
    [InlineData("Customer")]
    public void Another_or_finished_operation_cannot_authorize_a_transfer(string mismatch)
    {
        var operation = Operation();
        var expectedId = operation.OperationId;
        switch (mismatch)
        {
            case "OperationId":
                operation.OperationId = Guid.NewGuid();
                break;
            case "Settled":
                operation.SettledAt = DateTimeOffset.UtcNow;
                break;
            case "SourceKind":
                operation.SourceKind = "Account";
                break;
            case "Version":
                operation.PolicyVersion = 0;
                break;
            case "Country":
                operation.CountryCode = "ZW";
                break;
            case "Currency":
                operation.Currency = "USD";
                break;
            case "Customer":
                operation.CustomerId = Guid.NewGuid();
                break;
        }

        var error = Assert.Throws<PlatformException>(() => LedgerOperationPolicy.Require(operation, expectedId, Account(Source), false));
        Assert.Equal("country_operation_required", error.Code);
    }

    [Theory]
    [InlineData("Reversal")]
    [InlineData("DisputeFinance")]
    public void Historic_settlement_requires_a_claim_for_its_exact_original(string sourceKind)
    {
        var operation = Operation();
        operation.SourceKind = sourceKind;
        operation.Currency = "USD";
        operation.OriginalTransactionId = Guid.NewGuid();
        LedgerOperationPolicy.Require(
            operation,
            operation.OperationId,
            Account(Source, "USD", status: "Blocked"),
            true,
            operation.OriginalTransactionId);
        var error = Assert.Throws<PlatformException>(() => LedgerOperationPolicy.Require(
            operation,
            operation.OperationId,
            Account(Source, "USD", status: "Blocked"),
            true,
            Guid.NewGuid()));
        Assert.Equal("country_operation_required", error.Code);
    }

    [Theory]
    [InlineData("Account")]
    [InlineData("Connection")]
    public void Account_activation_requires_its_own_account_or_connection_claim(string sourceKind)
    {
        var operation = Operation();
        operation.OperationId = Source;
        operation.SourceKind = sourceKind;
        var account = new AccountEvent
        {
            AccountId = Source,
            UserId = User,
            CountryCode = "ZA",
            Currency = "ZAR",
            AccountType = "Cheque"
        };
        LedgerOperationPolicy.RequireAccount(operation, account);
        operation.OperationId = Guid.NewGuid();
        var error = Assert.Throws<PlatformException>(() => LedgerOperationPolicy.RequireAccount(operation, account));
        Assert.Equal("country_operation_required", error.Code);
    }

    private static CountryOperation Operation() => new()
    {
        RequestId = Guid.NewGuid(),
        OperationId = Guid.NewGuid(),
        SourceKind = "Payment",
        CustomerId = User,
        PolicyVersion = 1,
        CountryCode = "ZA",
        Currency = "ZAR"
    };

    private sealed class NoConfiguration : IFinancialConfiguration
    {
        public Task<PlatformSettings> GetFinancialConfiguration(CancellationToken ct) =>
            throw new InvalidOperationException("A posted replay must not read new policy.");

        public Task<CountryOperation> RequireOperation(Guid operationId, CancellationToken ct) =>
            throw new InvalidOperationException("A posted replay must not require a new operation claim.");
    }

    private sealed class NoWrites : ILedgerCommandStore
    {
        public Task<BalanceSnapshot> ReserveAccountFunds(ReserveRequest request, PlatformSettings settings, CancellationToken ct) =>
            throw new InvalidOperationException();

        public Task<JournalResult> Post(PostingPlan plan, PlatformSettings settings, CancellationToken ct) =>
            throw new InvalidOperationException("A posted replay must not post again.");

        public Task ReleaseReservedAccountFunds(Guid id, CancellationToken ct) => throw new InvalidOperationException();
    }

    private sealed class NoPlans : IPostingPlanFactory
    {
        public PostingPlan CreatePostingPlan(PostingContext context) =>
            throw new InvalidOperationException("A posted replay must not rebuild its plan.");
    }

    private sealed class PostedReads(JournalResult result) : ILedgerQueryStore
    {
        public Task<JournalResult?> TryPosted(PostJournalRequest request, CancellationToken ct) => Task.FromResult<JournalResult?>(result);

        public Task<LedgerPosition> ReadPosition(Guid id, CancellationToken ct) => throw new InvalidOperationException();

        public Task<JournalResult> GetJournal(Guid id, CancellationToken ct) => throw new InvalidOperationException();

        public Task<object> Entries(Guid id, Guid user, CancellationToken ct) => throw new InvalidOperationException();

        public Task<object> Reconcile(CancellationToken ct) => throw new InvalidOperationException();
    }
}
