using System.Security.Claims;
using System.Text.Json;
using DisputePortal.Api.Models.Configuration;
using DisputePortal.Api.Service.Banking;
using DisputePortal.Api.Service.BackgroundJobs;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace DisputePortal.UnitTests;

public class AssessmentAccountHistoryTests
{
    [Theory]
    [InlineData("Cheque", 18634.06)]
    [InlineData("Savings", 8634.06)]
    public void Opening_history_reconciles_and_contains_disputable_purchases_and_debit_orders(string type, decimal balance)
    {
        var options = Settings();
        options.Validate();
        var account = Account(type);
        var data = AssessmentAccountHistory.Create(account, options.Accounts.Single(item => item.Type == type), options.HistoryDays, DateTimeOffset.UtcNow);
        data.History.ValidateAccountHistorySnapshot();
        Assert.Equal(7, data.History.Transactions.Length);
        Assert.Equal(balance, data.History.ExpectedBalance);
        Assert.Equal(0, data.History.ExpectedReserved);
        Assert.Equal("assessment", data.History.SourceSystem);
        Assert.All(data.History.Transactions, item => Assert.Equal(account.Currency, item.Currency));
        Assert.Equal(2, data.History.Transactions.Count(item => item.Type == "CreditCardPayment"));
        Assert.All(data.History.Transactions.Where(item => item.Direction == "Debit"), item => Assert.True(item.DisputeEligible));
        Assert.All(data.DebitOrders.Values, item => Assert.Equal("EFT", item.Scheme));
    }

    [Fact]
    public void Retry_uses_the_same_account_transaction_and_event_identifiers()
    {
        var account = Account("Cheque");
        var template = Settings().Accounts[0];
        var at = DateTimeOffset.UtcNow;
        var first = AssessmentAccountHistory.Create(account, template, 30, at);
        var retry = AssessmentAccountHistory.Create(account, template, 30, at);
        Assert.Equal(first.History.Hash, retry.History.Hash);
        Assert.Equal(first.History.Transactions.Select(item => item.Id), retry.History.Transactions.Select(item => item.Id));
        Assert.NotEqual(AssessmentAccountHistory.StableId(account.Id, "request"), AssessmentAccountHistory.StableId(account.Id, "opening"));
        Assert.NotEqual(AssessmentAccountHistory.StableId(account.Id, "opening"), AssessmentAccountHistory.StableId(Guid.NewGuid(), "opening"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    public void Transaction_dates_stay_in_the_first_configured_days_after_opening(int ageDays)
    {
        var account = Account("Cheque");
        account.CreatedAt = DateTimeOffset.UtcNow.AddDays(-ageDays).AddSeconds(-1);
        var data = AssessmentAccountHistory.Create(account, Settings().Accounts[0], 30, DateTimeOffset.UtcNow);
        Assert.All(data.History.Transactions, item =>
        {
            Assert.True(item.OccurredAt >= account.CreatedAt.AddTicks(-10));
            Assert.True(item.OccurredAt < account.CreatedAt.AddDays(30));
            Assert.True(item.OccurredAt <= DateTimeOffset.UtcNow);
        });
    }

    [Theory]
    [InlineData("Active", 1, 0)]
    [InlineData("Active", 0, 1)]
    [InlineData("Blocked", 0, 0)]
    [InlineData("Closed", 0, 0)]
    public void Existing_balances_reserves_and_inactive_accounts_cannot_be_seeded(string status, decimal balance, decimal reserved)
    {
        var account = Account("Cheque");
        account.Status = status;
        account.LedgerBalance = balance;
        account.ReservedBalance = reserved;
        Assert.Throws<InvalidOperationException>(() => AssessmentAccountHistory.Create(account, Settings().Accounts[0], 30, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Altered_financial_snapshot_is_rejected()
    {
        var data = AssessmentAccountHistory.Create(Account("Cheque"), Settings().Accounts[0], 30, DateTimeOffset.UtcNow);
        data.History.Transactions[1].Amount++;
        Assert.Throws<InvalidOperationException>(data.History.ValidateAccountHistorySnapshot);
    }

    [Fact]
    public void Invalid_templates_reject_overdrafts_and_duplicate_identifiers()
    {
        var options = Settings();
        options.Accounts[0].Transactions[0].Amount = 1;
        Assert.Throws<InvalidOperationException>(options.Validate);
        options = Settings();
        options.Accounts[0].Transactions[1].Code = options.Accounts[0].Transactions[0].Code;
        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData("payments", false)]
    [InlineData("identity", true)]
    public void Assessment_import_requires_the_identity_service_and_matching_owner(string caller, bool otherOwner)
    {
        var account = Account("Cheque");
        var data = AssessmentAccountHistory.Create(account, Settings().Accounts[0], 30, DateTimeOffset.UtcNow);
        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("caller", caller),
            new Claim(ClaimTypes.NameIdentifier, (otherOwner ? Guid.NewGuid() : account.UserId).ToString())
        ], "test"));
        Assert.Throws<PlatformException>(() => AssessmentDataAccess.AuthorizeImport(user, data.History));
    }

    [Fact]
    public void Assessment_data_is_rejected_in_production_and_disabled_by_default()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Production");
        var disabled = new ConfigurationBuilder().Build();
        Assert.False(AssessmentDataAccess.IsEnabled(disabled, environment));
        var enabled = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Assessment:PopulateAccounts"] = "true" }).Build();
        Assert.Throws<InvalidOperationException>(() => AssessmentDataAccess.IsEnabled(enabled, environment));
    }

    [Fact]
    public async Task Disabled_worker_does_not_call_financial_services_or_open_a_database()
    {
        var client = Substitute.For<IServiceClient>();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Development");
        var worker = new AssessmentAccountWorker(
            new ServiceDatabase(new ServiceOptions { DatabaseProvider = "SqlServer" }), client, Settings(),
            new ConfigurationBuilder().Build(), environment, NullLogger<AssessmentAccountWorker>.Instance);
        await worker.PrepareCustomerAccounts(Guid.NewGuid(), default);
        Assert.Empty(client.ReceivedCalls());
    }

    private static AssessmentDataOptions Settings() => JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Configuration", "assessment-data.json")))
        .RootElement.GetProperty("AssessmentData").Deserialize<AssessmentDataOptions>()!;

    private static AccountRecord Account(string type) => new()
    {
        Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Number = "4798535553",
        Type = type, CountryCode = "ZA", Currency = "ZAR", Status = "Active",
        CreatedAt = DateTimeOffset.UtcNow.AddHours(-3)
    };
}
