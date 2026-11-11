namespace DisputePortal.Api.Models.Domain;

public class PushProvider
{
    public string Name { get; set; } = "";
    public string[] ExactHosts { get; set; } = [];
    public string[] SubdomainSuffixes { get; set; } = [];
}
