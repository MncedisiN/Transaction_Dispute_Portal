using DisputePortal.Api.Models.Configuration;
using DisputePortal.Api.Service.Banking;
using Xunit;

namespace DisputePortal.UnitTests;

public class RefundDescriptionTests
{
    [Theory]
    [InlineData("Dispute refund \u00C3\u201A\u00C2\u00B7 DP-2026-127125", "Dispute refund \u00B7 DP-2026-127125")]
    [InlineData("Refund recalled \u00C2\u00B7 DP-2026-127125 \u00C2\u00B7 RF-123", "Refund recalled \u00B7 DP-2026-127125 \u00B7 RF-123")]
    [InlineData("Card Purchase \u00B7 Caf\u00E9", "Card Purchase \u00B7 Caf\u00E9")]
    [InlineData("Dispute refund - DP-2026-127125", "Dispute refund - DP-2026-127125")]
    public void CustomerDescriptionPreservesReferencesAndValidText(string storedDescription, string expected)
    {
        var formatter = new TransactionPresentationFormatter(new TransactionPresentationRules());

        var displayed = formatter.Description("Refund", "Dispute", storedDescription);

        Assert.Equal(expected, displayed);
    }
}
