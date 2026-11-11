using System.ComponentModel.DataAnnotations;
using DisputePortal.Api.Models.Domain;

namespace DisputePortal.Api.Models.Requests;

public class SupportSendRequest
{
    public Guid? ThreadId { get; set; }
    public Guid? DisputeId { get; set; }

    [Required, MaxLength(200)]
    public string Subject { get; set; } = string.Empty;

    [Required, MinLength(2), MaxLength(2000)]
    public string Body { get; set; } = string.Empty;
    public Guid? TransactionId { get; set; }
    public Guid? ClientMessageId { get; set; }
    public Guid? RelatedDisputeId { get; set; }

    [MaxLength(32)]
    public string? ConcernCode { get; set; }
    public EnquiryDetails? Enquiry { get; set; }
    public bool StartNewConversation { get; set; }

    public SupportSendRequest()
    {
    }

    public SupportSendRequest(
        Guid? ThreadId,
        Guid? DisputeId,
        string Subject,
        string Body,
        Guid? TransactionId = null,
        Guid? ClientMessageId = null,
        Guid? RelatedDisputeId = null,
        string? ConcernCode = null,
        EnquiryDetails? Enquiry = null,
        bool StartNewConversation = false)
    {
        this.ThreadId = ThreadId;
        this.DisputeId = DisputeId;
        this.Subject = Subject;
        this.Body = Body;
        this.TransactionId = TransactionId;
        this.ClientMessageId = ClientMessageId;
        this.RelatedDisputeId = RelatedDisputeId;
        this.ConcernCode = ConcernCode;
        this.Enquiry = Enquiry;
        this.StartNewConversation = StartNewConversation;
    }

    public SupportSendRequest(SupportSendRequest other)
    {
        ThreadId = other.ThreadId;
        DisputeId = other.DisputeId;
        Subject = other.Subject;
        Body = other.Body;
        TransactionId = other.TransactionId;
        ClientMessageId = other.ClientMessageId;
        RelatedDisputeId = other.RelatedDisputeId;
        ConcernCode = other.ConcernCode;
        Enquiry = other.Enquiry;
        StartNewConversation = other.StartNewConversation;
    }
}
