using DisputePortal.Api.Models.Requests;
using FluentValidation;

namespace DisputePortal.Api.Models.Validators;

public class TransitionValidator : AbstractValidator<TransitionRequest>
{
    public TransitionValidator()
    {
        RuleFor(x => x.ExpectedVersion).GreaterThan(0);
        RuleFor(x => x.Status).NotEmpty();
    }
}
