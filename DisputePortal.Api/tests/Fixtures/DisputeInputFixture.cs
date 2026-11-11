using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Interfaces.Repository;

namespace DisputePortal.TestFixtures;

public sealed class DisputeInputFixture : IDisputeInputPolicyStore
{
    public Task<DisputeInputPolicy> GetDisputeInputPolicy(CancellationToken ct) => Task.FromResult(new DisputeInputPolicy
    {
        ReasonMaximumLength = 120,
        DescriptionMinimumLength = 10,
        DescriptionMaximumLength = 2000,
        CommentMaximumLength = 1000,
        NoteMinimumLength = 1,
        NoteMaximumLength = 4000,
        StatusMaximumLength = 32,
        IdempotencyKeyMinimumLength = 8,
        IdempotencyKeyMaximumLength = 128,
        Categories = [new global::DisputePortal.Api.Models.Domain.DisputeCategory {
            Code = "Unrecognized",
            Label = "Unrecognised",
            Reason = "I did not make this transaction",
            TransactionType = null,
            SortOrder = 1
        }, new global::DisputePortal.Api.Models.Domain.DisputeCategory {
            Code = "Duplicate",
            Label = "Duplicate",
            Reason = "I was charged more than once",
            TransactionType = null,
            SortOrder = 2
        }, new global::DisputePortal.Api.Models.Domain.DisputeCategory {
            Code = "IncorrectAmount",
            Label = "Incorrect amount",
            Reason = "The amount is incorrect",
            TransactionType = null,
            SortOrder = 3
        }, new global::DisputePortal.Api.Models.Domain.DisputeCategory {
            Code = "GoodsOrServices",
            Label = "Goods or services",
            Reason = "I did not receive the goods or service",
            TransactionType = null,
            SortOrder = 4
        }, new global::DisputePortal.Api.Models.Domain.DisputeCategory {
            Code = "DebitOrder",
            Label = "Debit order",
            Reason = "I cancelled this debit order",
            TransactionType = "DebitOrder",
            SortOrder = 5
        }, new global::DisputePortal.Api.Models.Domain.DisputeCategory {
            Code = "Other",
            Label = "Other",
            Reason = "Other",
            TransactionType = null,
            SortOrder = 6
        }]
    });
}
