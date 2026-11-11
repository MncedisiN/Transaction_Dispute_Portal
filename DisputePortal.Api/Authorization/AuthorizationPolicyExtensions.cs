namespace DisputePortal.Api.Authorization;

public static class AuthorizationPolicyExtensions
{
    public static IServiceCollection AddPortalAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy("monitoring.read", policy => policy.RequireAuthenticatedUser().RequireRole("super_admin"));
            options.AddPolicy(
                "transactions.assist",
                policy => policy.RequireAuthenticatedUser().RequireClaim("permission", "transactions.assist"));
            options.AddPolicy("users.manage", policy => policy.RequireAuthenticatedUser().RequireClaim("permission", "users.manage"));
            options.AddPolicy(
                "operations.manage",
                policy => policy.RequireAuthenticatedUser().RequireClaim("permission", "operations.manage"));
            options.AddPolicy("customer", policy => policy.RequireAuthenticatedUser().RequireRole("customer"));
            options.AddPolicy("disputes.read", policy => policy.RequireAuthenticatedUser().RequireClaim("permission", "disputes.read"));
            options.AddPolicy("refund.approve", policy => policy.RequireAuthenticatedUser().RequireClaim("permission", "refund.approve"));
            options.AddPolicy("disputes.manage", policy => policy.RequireAuthenticatedUser().RequireClaim("permission", "disputes.manage"));
            options.AddPolicy("audit.read", policy => policy.RequireAuthenticatedUser().RequireClaim("permission", "audit.read"));
        });
        return services;
    }
}
