using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Repository;
using Xunit;

namespace DisputePortal.UnitTests;

public sealed class GuidedEnquiryPolicyTests
{
    private static SupportSendRequest Request(string description) => new(
        null,
        null,
        "Account Enquiry",
        description,
        ClientMessageId: Guid.NewGuid(),
        ConcernCode: "Account",
        Enquiry: new global::DisputePortal.Api.Models.Domain.EnquiryDetails { Description = description });

    [Theory]
    [InlineData("password: this-is-my-secret")]
    [InlineData("My PIN: 1234")]
    [InlineData("OTP=123456")]
    [InlineData("My card is 4111 1111 1111 1111")]
    public void Security_values_are_rejected_before_persistence(string body) =>
        Assert.Throws<PortalValidationException>(() => SupportRepository.EnquiryRequestHash(Request(body)));

    [Theory]
    [InlineData("I forgot my PIN. Please explain the recovery process.")]
    [InlineData("I cannot reset my password through the application.")]
    public void Describing_a_security_problem_does_not_require_sharing_a_secret(string body) =>
        Assert.NotNull(SupportRepository.EnquiryRequestHash(Request(body)));

    [Fact]
    public void Complete_answers_have_a_stable_idempotency_fingerprint()
    {
        var request = Request("Please explain this account charge.");
        Assert.Equal(SupportRepository.EnquiryRequestHash(request), SupportRepository.EnquiryRequestHash(request));
        Assert.NotEqual(
            SupportRepository.EnquiryRequestHash(request),
            SupportRepository.EnquiryRequestHash(new SupportSendRequest(request)
            {
                Enquiry = new global::DisputePortal.Api.Models.Domain.EnquiryDetails
                {
                    Description = request.Body,
                    OccurredOn = new DateOnly(2026, 1, 1)
                }
            }));
    }

    [Fact]
    public void Message_body_must_match_the_reviewed_answers()
    {
        var request = Request("Please explain this charge.");
        Assert.Throws<PortalValidationException>(() => SupportRepository.EnquiryRequestHash(new SupportSendRequest(request) { Body = "A different complaint must not be saved." }));
        Assert.Throws<PortalValidationException>(() => SupportRepository.EnquiryRequestHash(new SupportSendRequest(request) { ThreadId = Guid.NewGuid() }));
        Assert.Throws<PortalValidationException>(() => SupportRepository.EnquiryRequestHash(new SupportSendRequest(request) { ClientMessageId = null }));
    }

    [Fact]
    public void Customer_wording_is_preserved_in_the_complete_message()
    {
        var value = new EnquiryDetails
        {
            Description = "  Please explain this charge.  ",
            AdditionalDetails = "  It appeared after cancellation.  "
        };
        Assert.Equal("Please explain this charge.\n\nIt appeared after cancellation.", value.MessageBody);
    }
}
