using System.Security.Claims;
using System.Text.Json;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Platform.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Payments.Interfaces;

namespace DisputePortal.Platform.Tests;

public sealed class PaymentCountryOperationTests
{
    [Fact]
    public async Task A_new_quote_preserves_amount_and_default_currency_without_requesting_a_conversion()
    {
        var scenario = new Scenario();
        var quote = await scenario.Service.Quote(new("Transfer", scenario.Source.Id, scenario.Target.Id, 10), scenario.User, default);
        Assert.Equal("ZAR", quote.Currency);
        Assert.Equal(quote.Currency, quote.TargetCurrency);
        Assert.Equal(quote.Amount, quote.TargetAmount);
        Assert.Null(quote.FxQuoteId);
        Assert.Null(quote.ExchangeRate);
        Assert.Null(quote.RateId);
        Assert.DoesNotContain(scenario.Log, item => item.StartsWith("configuration:/quotes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_saved_terminal_payment_is_replayed_before_any_remote_policy_read()
    {
        var scenario = new Scenario();
        scenario.Store.Saved = scenario.Payment("Completed");
        var replay = await scenario.Service.CreateInternalTransfer(scenario.Request, scenario.User, default);
        Assert.Same(scenario.Store.Saved, replay);
        Assert.Equal(new[] { "replay" }, scenario.Log);
    }

    [Fact]
    public async Task The_central_operation_identifier_is_used_for_the_saved_payment()
    {
        var scenario = new Scenario();
        scenario.Store.CreatedStatus = "Authorized";
        var payment = await scenario.Service.CreateInternalTransfer(scenario.Request, scenario.User, default);
        Assert.Equal(scenario.Client.Operation.OperationId, payment.Id);
        Assert.Equal(scenario.Request.RequestId, scenario.Client.Begin!.RequestId);
        Assert.Equal("Payment", scenario.Client.Begin.SourceKind);
        Assert.Equal(scenario.User, scenario.Client.Begin.CustomerId);
        Assert.Equal(scenario.Quote.SettingsVersion, scenario.Client.Begin.ExpectedVersion);
        Assert.Equal("ZA", scenario.Client.Begin.CountryCode);
        Assert.Equal("ZAR", scenario.Client.Begin.Currency);
        Assert.True(scenario.Log.IndexOf("begin") < scenario.Log.IndexOf("create"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_or_cancelled_local_creation_fences_the_request_before_settling(bool cancelled)
    {
        var scenario = new Scenario();
        scenario.Store.CreateError = cancelled ? new OperationCanceledException() : new IOException("Uncertain local write outcome.");
        scenario.Store.CancelOutcome = new(true, null);
        await Assert.ThrowsAnyAsync<Exception>(() => scenario.Service.CreateInternalTransfer(scenario.Request, scenario.User, default));
        Assert.True(scenario.Log.IndexOf("create") < scenario.Log.IndexOf("cancel_tombstone"));
        Assert.True(scenario.Log.IndexOf("cancel_tombstone") < scenario.Log.IndexOf("settle"));
        Assert.True(scenario.Store.CleanupTokenWasUsable);
    }

    [Fact]
    public async Task An_uncertain_create_does_not_settle_a_concurrently_saved_payment()
    {
        var scenario = new Scenario();
        scenario.Store.CreateError = new IOException("Uncertain local write outcome.");
        scenario.Store.CancelOutcome = new(false, scenario.Payment("Preparing"));
        await Assert.ThrowsAsync<IOException>(() => scenario.Service.CreateInternalTransfer(scenario.Request, scenario.User, default));
        Assert.Contains("cancel_tombstone", scenario.Log);
        Assert.DoesNotContain("settle", scenario.Log);
    }

    [Fact]
    public async Task An_uncertain_begin_response_is_cancelled_durably_before_a_late_local_create_can_start()
    {
        var scenario = new Scenario();
        scenario.Client.BeginError = new OperationCanceledException();
        await Assert.ThrowsAsync<OperationCanceledException>(() => scenario.Service.CreateInternalTransfer(
            scenario.Request,
            scenario.User,
            default));
        Assert.DoesNotContain("create", scenario.Log);
        Assert.True(scenario.Log.IndexOf("begin") < scenario.Log.IndexOf("cancel_tombstone"));
        Assert.True(scenario.Log.IndexOf("cancel_tombstone") < scenario.Log.IndexOf("settle"));
    }

    [Theory]
    [InlineData("Preparing")]
    [InlineData("Authorized")]
    public async Task Cancel_unknown_does_not_release_an_existing_nonterminal_payment(string status)
    {
        var scenario = new Scenario();
        scenario.Store.CancelOutcome = new(false, scenario.Payment(status));
        var result = await scenario.Service.CancelUnknown(scenario.User, scenario.Request.RequestId, default);
        Assert.False(result.Cancelled);
        Assert.DoesNotContain("settle", scenario.Log);
        Assert.DoesNotContain("release", scenario.Log);
    }

    [Fact]
    public async Task Cancel_unknown_settles_only_after_the_cancel_tombstone_is_committed()
    {
        var scenario = new Scenario();
        scenario.Store.CancelOutcome = new(true, null);
        await scenario.Service.CancelUnknown(scenario.User, scenario.Request.RequestId, default);
        Assert.Equal(new[] { "cancel_tombstone", "settle" }, scenario.Log);
    }

    [Fact]
    public async Task Preparing_failure_releases_funds_before_its_terminal_event_or_policy_settlement()
    {
        var scenario = new Scenario();
        scenario.Store.Saved = scenario.Payment("Preparing");
        scenario.Client.ReserveError = new PlatformException(409, "insufficient_funds", "Insufficient funds.");
        await Assert.ThrowsAsync<PlatformException>(() => scenario.Service.CreateInternalTransfer(scenario.Request, scenario.User, default));
        Assert.True(scenario.Log.IndexOf("failure_tombstone") < scenario.Log.IndexOf("release"));
        Assert.True(scenario.Log.IndexOf("release") < scenario.Log.IndexOf("failure_event"));
        Assert.True(scenario.Log.IndexOf("failure_event") < scenario.Log.IndexOf("settle"));
    }

    [Fact]
    public async Task A_release_failure_keeps_the_operation_pending_until_durable_recovery_completes()
    {
        var scenario = new Scenario();
        scenario.Store.Saved = scenario.Payment("Preparing");
        scenario.Client.ReserveError = new PlatformException(409, "insufficient_funds", "Insufficient funds.");
        scenario.Client.ReleaseError = new IOException("Ledger release outcome unavailable.");
        await Assert.ThrowsAsync<IOException>(() => scenario.Service.CreateInternalTransfer(scenario.Request, scenario.User, default));
        Assert.Equal("Failed", scenario.Store.Saved!.Status);
        Assert.DoesNotContain("failure_event", scenario.Log);
        Assert.DoesNotContain("settle", scenario.Log);
        scenario.Client.ReleaseError = null;
        await scenario.Service.RunScheduledWork(default);
        Assert.Contains("failure_event", scenario.Log);
        Assert.Contains("settle", scenario.Log);
    }

    private sealed class Scenario
    {
        public readonly Guid User = Guid.NewGuid();
        public readonly List<string> Log = [];
        public readonly AccountRecord Source;
        public readonly AccountRecord Target;
        public readonly PaymentRequest Request;
        public readonly PaymentQuote Quote;
        public readonly FakeStore Store;
        public readonly FakeClient Client;
        public readonly PaymentService Service;

        public Scenario()
        {
            Source = new global::DisputePortal.Platform.Contracts.AccountRecord
            {
                Id = Guid.NewGuid(),
                UserId = User,
                Number = "100000000001",
                Type = "Cheque",
                CountryCode = "ZA",
                Currency = "ZAR",
                Status = "Active",
                LedgerBalance = 1000,
                ReservedBalance = 0,
                Version = 0,
                CreatedAt = DateTimeOffset.UtcNow
            };
            Target = new global::DisputePortal.Platform.Contracts.AccountRecord(Source)
            {
                Id = Guid.NewGuid(),
                Number = "100000000002"
            };
            Request = new(Guid.NewGuid(), "Transfer", Source.Id, Target.Id, 10, QuoteId: Guid.NewGuid());
            var settings = JsonSerializer.Deserialize<PlatformSettings>(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "seed-settings.json")),
                DomainEvent.Json)!;
            Quote = new global::DisputePortal.Platform.Contracts.PaymentQuote
            {
                Id = Request.QuoteId!.Value,
                UserId = User,
                Kind = "Transfer",
                AccountId = Source.Id,
                RecipientAccountId = Target.Id,
                Amount = 10,
                Currency = "ZAR",
                Fee = 0,
                TargetAmount = 10,
                TargetCurrency = "ZAR",
                FxQuoteId = null,
                SettingsVersion = settings.Version,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
            };
            Store = new(this);
            Client = new(this, settings);
            Service = new(Store, Client, NullLogger<PaymentService>.Instance, TestMigrationConfiguration.ApplyServiceMigrationSettings(new ServiceOptions()));
        }

        public PaymentRecord Payment(string status, Guid? id = null) => new global::DisputePortal.Platform.Contracts.PaymentRecord
        {
            Id = id ?? Guid.NewGuid(),
            UserId = User,
            RequestId = Request.RequestId,
            Kind = "Transfer",
            AccountId = Source.Id,
            RecipientAccountId = Target.Id,
            Amount = 10,
            Currency = "ZAR",
            Fee = 0,
            TargetAmount = 10,
            TargetCurrency = "ZAR",
            Status = status,
            Reference = "DP-EXISTING-TRANSFER",
            CreatedAt = DateTimeOffset.UtcNow,
            SettingsVersion = Quote.SettingsVersion,
            QuoteExpiresAt = Quote.ExpiresAt
        };
    }

    private sealed class FakeStore(Scenario scenario) : IPaymentStore
    {
        public PaymentRecord? Saved;
        public string CreatedStatus = "Preparing";
        public Exception? CreateError;
        public PaymentRequestOutcome CancelOutcome = new(true, null);
        public bool CleanupTokenWasUsable;

        public Task<PaymentRecord?> Replay(Guid user, PaymentRequest request, CancellationToken ct)
        {
            scenario.Log.Add("replay");
            return Task.FromResult(Saved);
        }

        public Task<PaymentQuote> ReadQuote(Guid user, PaymentRequest request, CancellationToken ct)
        {
            scenario.Log.Add("quote");
            return Task.FromResult(scenario.Quote);
        }

        public Task<PaymentRecord> InsertPaymentIntent(Guid user, PaymentRequest request, CountryOperation operation, CancellationToken ct)
        {
            scenario.Log.Add("create");
            if (CreateError is not null)
            {
                throw CreateError;
            }

            Saved = scenario.Payment(CreatedStatus, operation.OperationId);
            return Task.FromResult(Saved!);
        }

        public Task<PaymentRequestOutcome> CancelUnknown(Guid user, Guid request, CancellationToken ct)
        {
            scenario.Log.Add("cancel_tombstone");
            CleanupTokenWasUsable = !ct.IsCancellationRequested;
            return Task.FromResult(CancelOutcome);
        }

        public Task<bool> Fail(Guid id, string code, CancellationToken ct)
        {
            scenario.Log.Add("failure_tombstone");
            Saved = new global::DisputePortal.Platform.Contracts.PaymentRecord(Saved!)
            {
                Status = "Failed",
                FailureCode = code
            };
            return Task.FromResult(true);
        }

        public Task FinishFailure(Guid id, CancellationToken ct)
        {
            scenario.Log.Add("failure_event");
            return Task.CompletedTask;
        }

        public Task<PaymentRecord[]> Preparing(CancellationToken ct) =>
            Task.FromResult(Saved is null ? Array.Empty<PaymentRecord>() : new[] { Saved! });

        public Task<PaymentRecord> Lookup(Guid user, Guid request, CancellationToken ct) => Task.FromResult(Saved!);

        public Task SaveQuote(PaymentQuote quote, CancellationToken ct) => Task.CompletedTask;

        public Task<PaymentRecord> Activate(Guid id, CancellationToken ct)
        {
            Saved = new global::DisputePortal.Platform.Contracts.PaymentRecord(Saved!)
            {
                Status = "Authorized"
            };
            return Task.FromResult(Saved!);
        }

        public Task<object> GetCustomerPayments(Guid user, CancellationToken ct) => throw new NotSupportedException();

        public Task CompletePaymentIntent(DomainEvent value, CancellationToken ct) => throw new NotSupportedException();

        public Task<CountryChangeEligibility> CountryEligibility(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeClient(Scenario scenario, PlatformSettings settings) : IServiceClient
    {
        public CountryOperationRequest? Begin;
        public Exception? BeginError;
        public Exception? ReserveError;
        public Exception? ReleaseError;
        public readonly CountryOperation Operation = new()
        {
            RequestId = scenario.Request.RequestId,
            OperationId = Guid.NewGuid(),
            SourceKind = "Payment",
            CustomerId = scenario.User,
            CountryCode = "ZA",
            Currency = "ZAR",
            PolicyVersion = settings.Version
        };

        public Task<T> SendServiceRequest<T>(
            string service,
            HttpMethod method,
            string path,
            object? body = null,
            ClaimsPrincipal? actor = null,
            CancellationToken ct = default)
        {
            object result;
            if (service == "configuration" && path == "/settings")
            {
                result = settings;
            }
            else if (service == "accounts")
            {
                result = path.EndsWith(scenario.Source.Id.ToString(), StringComparison.Ordinal) ? scenario.Source : scenario.Target;
            }
            else if (service == "configuration" && path == "/country-operations")
            {
                scenario.Log.Add("begin");
                Begin = Assert.IsType<CountryOperationRequest>(body);
                if (BeginError is not null)
                {
                    throw BeginError;
                }

                result = Operation;
            }
            else if (service == "configuration" && path.Contains("/settle?customerId=", StringComparison.Ordinal))
            {
                Assert.EndsWith(scenario.User.ToString(), path, StringComparison.Ordinal);
                scenario.Log.Add("settle");
                result = new object();
            }
            else if (service == "ledger" && path.EndsWith("/release", StringComparison.Ordinal))
            {
                scenario.Log.Add("release");
                if (ReleaseError is not null)
                {
                    throw ReleaseError;
                }

                result = new object();
            }
            else if (service == "ledger")
            {
                if (path == "/reservations" && ReserveError is not null)
                {
                    throw ReserveError;
                }

                var balance = new BalanceSnapshot
                {
                    AccountId = scenario.Source.Id,
                    UserId = scenario.User,
                    Currency = "ZAR",
                    LedgerBalance = 1000,
                    ReservedBalance = 0,
                    Version = 0
                };
                result = typeof(T) == typeof(BalanceSnapshot[]) ? new[]
                {
                    balance
                }

                : balance;
            }
            else
            {
                scenario.Log.Add(service + ":" + path);
                throw new InvalidOperationException("Unexpected remote call.");
            }

            return Task.FromResult((T)result);
        }
    }
}
