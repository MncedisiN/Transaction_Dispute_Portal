using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class DeletePolicyVersionRequest
{
    public long ConcurrencyToken { get; set; }

    [Required, StringLength(500, MinimumLength = 10)]
    public string Reason { get; set; } = "";
}
