using DisputePortal.Api.Configuration;
using System.Reflection;
using System.Security.Claims;
using DisputePortal.Api.Models.Responses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Services;

namespace DisputePortal.Api.Realtime; 
public class RealtimeMutationFilter(
    IRealtimePublisher publisher,
    IRealtimeRecipients recipients,
    ILogger<RealtimeMutationFilter> logger,
    ApiRuntimeOptions runtime) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();
        var http = context.HttpContext;
        if (executed.Exception is not null
            || executed.Canceled
            || !IsMutation(http.Request.Method)
            || !Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor))
        {
            return;
        }

        var status = (executed.Result as IStatusCodeActionResult)?.StatusCode ?? 200;
        if (status is < 200 or >= 300)
        {
            return;
        }

        var controller = context.ActionDescriptor.RouteValues.TryGetValue("controller", out var controllerName) ? controllerName ?? "" : "";
        var path = http.Request.Path.Value ?? "";
        var topics = Topics(controller, path);
        if (topics.Length == 0)
        {
            return;
        }

        if ((executed.Result as ObjectResult)?.Value is CreateDisputeResult { Replayed: true })
        {
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(runtime.RealtimeMutationTimeoutSeconds));
        try
        {
            var users = new HashSet<Guid>
            {
                actor
            };
            var routeId = RouteId(context, "disputeId") ?? RouteId(context, "id");
            var resultId = ResultId(executed.Result);
            var resourceId = routeId;
            var staff = controller is "Disputes" or "Admin" or "StaffTransactions" or "Evidence" or "Operations" or "DisputeRules";
            string? entity = null;
            if (controller is "Disputes" or "Admin" or "Evidence"
                || (controller == "Finance"
                && path.Contains("/disputes/", StringComparison.Ordinal))
                || (controller == "NotificationSettings"
                && path.Contains("/cases/", StringComparison.Ordinal)))
            {
                entity = "dispute";
                resourceId = resultId ?? routeId;
                staff = true;
            }
            else if (controller == "StaffTransactions")
            {
                entity = "dispute";
                resourceId = resultId;
            }
            else if (controller == "Operations" && path.Contains("/cases/", StringComparison.Ordinal))
            {
                entity = "dispute";
            }
            else if (controller == "Operations" && path.Contains("/users/", StringComparison.Ordinal) && routeId is { } target)
            {
                users.Add(target);
            }
            else if (controller == "Support" && !path.EndsWith("/read", StringComparison.Ordinal))
            {
                entity = "support";
                resourceId = resultId ?? routeId;
                staff = true;
            }
            else if (controller == "AccountManagement" && path.Contains("/requests/", StringComparison.Ordinal))
            {
                entity = "deletion";
            }

            if (controller == "AccountManagement")
            {
                staff = true;
            }

            if (entity is not null && resourceId is { } id)
            {
                var owner = await recipients.Owner(entity, id, timeout.Token);
                if (owner.HasValue)
                {
                    users.Add(owner.Value);
                }
            }

            await publisher.PublishPortalUpdate(users.ToArray(), staff, topics, resourceId, timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            logger.LogWarning("Live mutation invalidation deferred ({FailureType})", error.GetType().Name);
        }
    }

    public static bool IsMutation(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    private static string[] Topics(string controller, string path) => controller switch
    {
        "Disputes" or "Admin" or "StaffTransactions" or "Evidence" => ["disputes", "support", "notifications", "banking"],
        "Support" => path.EndsWith("/read", StringComparison.Ordinal) ? ["support"] : ["support", "disputes", "notifications"],
        "Operations" or "DisputeRules" => ["administration", "disputes", "notifications"],
        "Finance" => ["banking", "disputes", "notifications"],
        "BankingProvisioning" or "Transactions" or "Accounts" => ["banking"],
        "Profile" or "Password" => ["profile"],
        "NotificationSettings" when path.Contains("/cases/", StringComparison.Ordinal) => ["disputes", "support", "notifications"],
        "Notifications" or "NotificationSettings" => ["notifications"],
        "AccountManagement" => ["profile", "banking", "administration"],
        _ => []
    };

    private static Guid? RouteId(ActionExecutingContext context, string name) =>
        context.RouteData.Values.TryGetValue(name, out var value) && Guid.TryParse(value?.ToString(), out var id) ? id : null;

    private static Guid? ResultId(IActionResult? result)
    {
        if (result is not ObjectResult { Value: { } value })
        {
            return null;
        }

        if (value is CreateDisputeResult created)
        {
            return created.Id;
        }

        if (value is DisputeDetailDto detail)
        {
            return detail.Dispute.Id;
        }

        var property = value.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return property?.GetValue(value) is Guid id ? id : null;
    }
}
