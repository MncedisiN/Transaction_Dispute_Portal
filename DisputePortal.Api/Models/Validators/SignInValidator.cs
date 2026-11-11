using DisputePortal.Api.Models.Requests;
using FluentValidation;

namespace DisputePortal.Api.Models.Validators;

public class SignInValidator : AbstractValidator<SignInRequest>
{
    public SignInValidator()
    {
        RuleFor(x => x.LoginIdentifier).NotEmpty().MaximumLength(254);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(256);
    }
}
