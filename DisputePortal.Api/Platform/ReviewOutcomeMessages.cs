namespace DisputePortal.Api.Platform;

internal static class ReviewOutcomeMessages
{
    internal static string Describe(IConfiguration configuration, string status, string? code) =>
        (code is null ? null : configuration["ReviewOutcomes:" + code]) ?? configuration["ReviewOutcomes:" + status] ?? configuration["ReviewOutcomes:Default"] ?? "Open the original transaction to view the review outcome.";
}
