using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Notifications;
using Xunit;

namespace DisputePortal.Platform.Tests;

public sealed class NotificationScopeTests
{
    [Theory]
    [InlineData("Transfer", false)]
    [InlineData("Deposit", false)]
    [InlineData("Withdrawal", false)]
    [InlineData("Refund", true)]
    [InlineData("Recall", true)]
    public void Only_dispute_financial_results_create_notifications(string kind, bool expected)
    {
        var transaction = new TransactionEvent
        {
            TransactionId = Guid.NewGuid(),
            PaymentId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Kind = kind,
            Reference = "private-reference",
            AccountId = Guid.NewGuid(),
            RecipientAccountId = null,
            Amount = 10m,
            Currency = "ZAR",
            TargetAmount = null,
            TargetCurrency = null,
            Fee = 0,
            Status = "Completed"
        };
        Assert.Equal(
            expected,
            NotificationStore.Supports(DomainEvent.CreateDomainEvent(
                "transactions",
                EventTypes.TransactionCompleted,
                transaction,
                Guid.NewGuid())));
        Assert.Equal(
            expected,
            NotificationStore.Supports(DomainEvent.CreateDomainEvent(
                "transactions",
                EventTypes.TransactionFailed,
                new global::DisputePortal.Platform.Contracts.TransactionEvent(transaction) { Status = "Failed" },
                Guid.NewGuid())));
    }

    [Fact]
    public void Account_activation_and_incoming_payment_events_are_not_notifications()
    {
        Assert.False(NotificationStore.Supports(DomainEvent.CreateDomainEvent(
            "accounts",
            EventTypes.AccountActivated,
            new
            {
            },
            Guid.NewGuid())));
        Assert.False(NotificationStore.Supports(DomainEvent.CreateDomainEvent(
            "transactions",
            "transaction.received",
            new
            {
            },
            Guid.NewGuid())));
    }
}
