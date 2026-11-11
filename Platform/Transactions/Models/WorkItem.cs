using System.Text.Json;
using DisputePortal.Platform.Contracts;

namespace DisputePortal.Platform.Transactions.Models;

public class WorkItem
{
    public Guid Id { get; set; }
    public string PaymentBody { get; set; } = "";
    public string Status { get; set; } = "";
    public Guid LeaseId { get; set; }
    public int Attempts { get; set; }
    public string? FailureCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public WorkItem()
    {
    }

    public WorkItem(
        Guid id,
        string paymentBody,
        string status,
        Guid leaseId,
        int attempts,
        string? failureCode,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        PaymentBody = paymentBody;
        Status = status;
        LeaseId = leaseId;
        Attempts = attempts;
        FailureCode = failureCode;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public PaymentEvent Payment => JsonSerializer.Deserialize<PaymentEvent>(PaymentBody, DomainEvent.Json)!;
}
