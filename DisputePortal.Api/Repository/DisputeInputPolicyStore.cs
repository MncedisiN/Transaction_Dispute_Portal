using Dapper;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Interfaces.Repository;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository; 
public class DisputeInputPolicyStore(IDatabaseConnectionFactory connections) : IDisputeInputPolicyStore
{
    private DisputeInputPolicy? snapshot;

    public async Task<DisputeInputPolicy> GetDisputeInputPolicy(CancellationToken ct)
    {
        if (snapshot is not null)
        {
            return snapshot;
        }

        await using var connection = await connections.OpenConnection(ct);
        using var rows = await connection.QueryMultipleAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeInputPolicyStore/GetDisputeInputPolicies"),
            new { Enabled = true },
            cancellationToken: ct));
        var limits = await rows.ReadSingleAsync<Limits>();
        var categoryRows = (await rows.ReadAsync<CategoryRow>()).ToArray();
        var associations = (await rows.ReadAsync<CategoryTypeRow>()).ToLookup(
            row => row.CategoryCode,
            row => row.TransactionType,
            StringComparer.Ordinal);
        var categories = categoryRows.Select(category =>
        {
            var types = associations[category.Code].ToArray();
            // Retain the existing single-type response field for older clients.
            return new DisputeCategory
            {
                Code = category.Code,
                Label = category.Label,
                Reason = category.Reason,
                TransactionType = types.Length == 1 ? types[0] : null,
                SortOrder = category.SortOrder,
                TransactionTypes = types
            };
        }).ToArray();
        if (categories.Length == 0)
        {
            throw new InvalidOperationException("The dispute input catalogue is unavailable.");
        }

        return snapshot = new global::DisputePortal.Api.Models.Domain.DisputeInputPolicy
        {
            ReasonMaximumLength = limits.ReasonMaximumLength,
            DescriptionMinimumLength = limits.DescriptionMinimumLength,
            DescriptionMaximumLength = limits.DescriptionMaximumLength,
            CommentMaximumLength = limits.CommentMaximumLength,
            NoteMinimumLength = limits.NoteMinimumLength,
            NoteMaximumLength = limits.NoteMaximumLength,
            StatusMaximumLength = limits.StatusMaximumLength,
            IdempotencyKeyMinimumLength = limits.IdempotencyKeyMinimumLength,
            IdempotencyKeyMaximumLength = limits.IdempotencyKeyMaximumLength,
            Categories = categories
        };
    }

    private class CategoryRow
    {
        public string Code { get; set; } = "";
        public string Label { get; set; } = "";
        public string Reason { get; set; } = "";
        public int SortOrder { get; set; }
    }

    private class CategoryTypeRow
    {
        public string CategoryCode { get; set; } = "";
        public string TransactionType { get; set; } = "";
    }

    private class Limits
    {
        public int ReasonMaximumLength { get; set; }
        public int DescriptionMinimumLength { get; set; }
        public int DescriptionMaximumLength { get; set; }
        public int CommentMaximumLength { get; set; }
        public int NoteMinimumLength { get; set; }
        public int NoteMaximumLength { get; set; }
        public int StatusMaximumLength { get; set; }
        public int IdempotencyKeyMinimumLength { get; set; }
        public int IdempotencyKeyMaximumLength { get; set; }

        public Limits()
        {
        }

        public Limits(
            int ReasonMaximumLength,
            int DescriptionMinimumLength,
            int DescriptionMaximumLength,
            int CommentMaximumLength,
            int NoteMinimumLength,
            int NoteMaximumLength,
            int StatusMaximumLength,
            int IdempotencyKeyMinimumLength,
            int IdempotencyKeyMaximumLength)
        {
            this.ReasonMaximumLength = ReasonMaximumLength;
            this.DescriptionMinimumLength = DescriptionMinimumLength;
            this.DescriptionMaximumLength = DescriptionMaximumLength;
            this.CommentMaximumLength = CommentMaximumLength;
            this.NoteMinimumLength = NoteMinimumLength;
            this.NoteMaximumLength = NoteMaximumLength;
            this.StatusMaximumLength = StatusMaximumLength;
            this.IdempotencyKeyMinimumLength = IdempotencyKeyMinimumLength;
            this.IdempotencyKeyMaximumLength = IdempotencyKeyMaximumLength;
        }
    }
}
