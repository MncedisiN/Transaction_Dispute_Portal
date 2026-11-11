namespace DisputePortal.Platform.Contracts;

public class PaymentRequestOutcome
{
    public bool Cancelled { get; set; }
    public PaymentRecord? Payment { get; set; }

    public PaymentRequestOutcome()
    {
    }

    public PaymentRequestOutcome(bool Cancelled, PaymentRecord? Payment)
    {
        this.Cancelled = Cancelled;
        this.Payment = Payment;
    }
}
