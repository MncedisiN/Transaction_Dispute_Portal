using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Ledger.Interfaces;

namespace DisputePortal.Platform.Ledger.Domain;

public class PostingPlanFactory(IEnumerable<IPostingStrategy> strategies) : IPostingPlanFactory
{
    private readonly IReadOnlyDictionary<string, IPostingStrategy> handlers = strategies.ToDictionary(s => s.Kind, StringComparer.Ordinal);

    public PostingPlan CreatePostingPlan(PostingContext context)
    {
        if (!handlers.TryGetValue(context.Request.Kind, out var strategy))
        {
            throw new PlatformException(400, "unsupported_operation", "This financial operation is not supported.");
        }

        var plan = strategy.BuildPostingPlan(context);
        var entryIds = new HashSet<Guid>();
        if (plan.Lines.Any(line => !entryIds.Add(line.Id)))
        {
            throw new InvalidOperationException("Ledger entry identifiers must be unique within a journal.");
        }

        if (plan.Lines.Count < 2
            || plan.Lines.Any(v => v.Amount <= 0)
            || plan.Lines.GroupBy(v => v.Currency).Any(g => g.Sum(v => v.Side == "D" ? v.Amount : -v.Amount) != 0))
        {
            throw new InvalidOperationException("A posting plan must balance separately in each currency.");
        }

        return plan;
    }
}
