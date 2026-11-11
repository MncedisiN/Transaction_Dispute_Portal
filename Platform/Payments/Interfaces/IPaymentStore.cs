using DisputePortal.Platform.Contracts;

namespace DisputePortal.Platform.Payments.Interfaces;

public interface IPaymentStore
{
    Task<PaymentRecord> Lookup(Guid user, Guid request, CancellationToken ct);

    Task<PaymentRequestOutcome> CancelUnknown(Guid user, Guid request, CancellationToken ct);

    Task SaveQuote(PaymentQuote quote, CancellationToken ct);

    Task<PaymentRecord?> Replay(Guid user, PaymentRequest request, CancellationToken ct);

    Task<PaymentQuote> ReadQuote(Guid user, PaymentRequest request, CancellationToken ct);

    Task<PaymentRecord> InsertPaymentIntent(Guid user, PaymentRequest request, CountryOperation operation, CancellationToken ct);

    Task<CountryChangeEligibility> CountryEligibility(CancellationToken ct);

    Task FinishFailure(Guid id, CancellationToken ct);

    Task<object> GetCustomerPayments(Guid user, CancellationToken ct);

    Task CompletePaymentIntent(DomainEvent value, CancellationToken ct);

    Task<PaymentRecord> Activate(Guid id, CancellationToken ct);

    Task<bool> Fail(Guid id, string code, CancellationToken ct);

    Task<PaymentRecord[]> Preparing(CancellationToken ct);
}
