namespace DisputePortal.Api.Models.Requests;

public class ResendSignInRequest
{
    public Guid ChallengeId { get; set; }

    public ResendSignInRequest()
    {
    }

    public ResendSignInRequest(Guid ChallengeId)
    {
        this.ChallengeId = ChallengeId;
    }
}
