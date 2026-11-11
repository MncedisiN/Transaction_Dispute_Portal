namespace DisputePortal.Api.Models.Requests;

public class SupportReadRequest
{
    public Guid MessageId { get; set; }

    public SupportReadRequest()
    {
    }

    public SupportReadRequest(Guid MessageId)
    {
        this.MessageId = MessageId;
    }
}
