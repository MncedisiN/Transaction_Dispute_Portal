namespace DisputePortal.Api.Service.Push;
// Provider error bodies can contain subscription identifiers; only their status
// and headers are needed for delivery decisions.
public class PushResponseHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var content = response.Content;
            response.Content = new StringContent("");
            content.Dispose();
        }

        return response;
    }
}
