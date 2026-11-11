using System.Text.Json.Serialization;
using DisputePortal.Api.Models.Responses;

namespace DisputePortal.Api.Models.Domain;

public class PasswordSignInResult
{
    public bool PasswordAccepted { get; set; }
    public Guid? UserId { get; set; }

    [JsonIgnore]
    public LoginSessionGrant? Session { get; set; }
    public IdentityChallengeDto? Challenge { get; set; }

    [JsonIgnore]
    public string? FlowCookie { get; set; }
}
