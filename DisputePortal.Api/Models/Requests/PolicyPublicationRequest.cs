using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class PolicyPublicationRequest
{
    public long ConcurrencyToken { get; set; }
    public Guid RequestId { get; set; }
    public int Priority { get; set; }
    public DateTimeOffset? EffectiveFromUtc { get; set; }
    public DateTimeOffset? EffectiveToUtc { get; set; }
    public Guid? SourceVersionPublicId { get; set; }
    public string Operation { get; set; } = "Create";

    [Required, StringLength(500, MinimumLength = 10)]
    public string Reason { get; set; } = "";
}
