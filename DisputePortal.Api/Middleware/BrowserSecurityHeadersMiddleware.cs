using System.Security.Cryptography;

namespace DisputePortal.Api.Middleware;

public class BrowserSecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    public async Task Invoke(HttpContext context)
    {
        var swagger = environment.IsDevelopment() && context.Request.Path.StartsWithSegments("/swagger");
        var nonce = swagger ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)) : null;
        if (nonce is not null)
        {
            context.Items["SwaggerNonce"] = nonce;
        }

        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.CacheControl = "private, no-store";
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            headers.ContentSecurityPolicy = swagger ? $"default-src 'self'; script-src 'self' 'nonce-{nonce}'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; object-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'" : "default-src 'none'; object-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
            return Task.CompletedTask;
        });
        await next(context);
    }
}
