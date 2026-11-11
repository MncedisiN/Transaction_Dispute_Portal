using DisputePortal.Api.Models.Requests;
using FluentValidation;

namespace DisputePortal.Api.Models.Validators;

public class AddNoteValidator : AbstractValidator<AddNoteRequest>
{
    public AddNoteValidator()
    {
        RuleFor(x => x.Content).NotEmpty();
        RuleFor(x => x.Visibility).Must(x => x is "Customer" or "Internal");
    }
}
