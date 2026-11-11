using System.Security.Claims;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DisputePortal.Platform.Infrastructure;

public static class AssessmentDataAccess
{
    public static bool IsEnabled(IConfiguration configuration, IHostEnvironment environment)
    {
        var enabled = configuration.GetValue<bool>("Assessment:PopulateAccounts");
        if (enabled && !environment.IsDevelopment())
        {
            throw new InvalidOperationException("Assessment account data is available only in Development.");
        }

        return enabled;
    }

    public static void AuthorizeImport(ClaimsPrincipal user, AccountHistorySnapshot history)
    {
        user.RequireCaller("identity");
        if (history.SourceSystem != "assessment"
            || history.ActorId != history.UserId
            || history.UserId != user.UserId())
        {
            throw new PlatformException(403, "assessment_owner_mismatch", "The assessment account owner does not match.");
        }

        history.ValidateAccountHistorySnapshot();
    }
}
