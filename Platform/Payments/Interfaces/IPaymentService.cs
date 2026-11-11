using DisputePortal.Platform.Contracts;

namespace DisputePortal.Platform.Payments.Interfaces;

public interface IPaymentService
{
    Task<PaymentQuote> Quote(PaymentQuoteRequest request, Guid user, CancellationToken ct);

    Task<PaymentRecord> CreateInternalTransfer(PaymentRequest request, Guid user, CancellationToken ct);

    Task<PaymentRequestOutcome> CancelUnknown(Guid user, Guid request, CancellationToken ct);
}
