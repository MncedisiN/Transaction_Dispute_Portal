using DisputePortal.Platform.Contracts;

namespace DisputePortal.Api.Models.Responses;

public class PolicyPreviewDto<T>
    where T : PolicyVersionMetadata
{
    public PolicyResolution<T> Before { get; set; } = new();
    public PolicyResolution<T> After { get; set; } = new();
    public PolicyResolution<T> AtActivation { get; set; } = new();
    public T Proposed { get; set; } = null!;
    public string Consequence { get; set; } = "Older versions never resume after supersession.";
}
