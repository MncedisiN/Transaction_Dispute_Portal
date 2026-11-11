using DisputePortal.Api.Models.Requests;
using FluentValidation;

namespace DisputePortal.Api.Models.Validators;

public class CreateDisputeValidator : AbstractValidator<CreateDisputeRequest>
{
    public CreateDisputeValidator()
    {
        RuleFor(x => x.TransactionId).NotEmpty();
        RuleFor(x => x.Category).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty();
        RuleFor(x => x.Description).NotEmpty();
    }
}
