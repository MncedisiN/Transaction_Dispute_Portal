using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Models.Configuration;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DisputePortal.Api.Middleware;

public class EvidenceUploadLimitsFilter(IEvidencePolicyRepository policies) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext;
        var policy = await policies.GetEvidencePolicy(request.RequestAborted);
        var bodyLimit = request.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodyLimit is { IsReadOnly: false })
        {
            bodyLimit.MaxRequestBodySize = policy.MaximumRequestBytes;
        }

        request.Features.Set<IFormFeature>(new FormFeature(request.Request, new FormOptions
        {
            MultipartBodyLengthLimit = policy.MaximumRequestBytes
        }));
        request.Items[typeof(EvidencePolicy)] = policy;
        await next();
    }
}
