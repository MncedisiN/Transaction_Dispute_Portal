using Microsoft.OpenApi.Models;

namespace DisputePortal.Api.Swagger;

public static class SwaggerServiceCollectionExtensions
{
    public static IServiceCollection AddPortalSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new()
            {
                Title = "Transactions Dispute Portal API",
                Version = "v1"
            });
            options.AddSecurityDefinition(
                "Bearer",
                new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "OAuth 2.0 / OpenID Connect access token."
                });
        });
        return services;
    }
}
