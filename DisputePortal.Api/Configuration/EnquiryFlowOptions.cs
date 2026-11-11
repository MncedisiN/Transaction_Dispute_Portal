namespace DisputePortal.Api.Configuration;

public class EnquiryFlowOptions
{
    public string InitialStep { get; set; } = string.Empty;
    public string RelatedCaseInitialStep { get; set; } = string.Empty;
    public string ReviewStep { get; set; } = string.Empty;
    public List<EnquiryStepOptions> Steps { get; set; } = [];

    public void Validate(DisputePortal.Api.Models.Configuration.PortalSettingsValidation validation)
    {
        var controls = validation.EnquiryControlKinds;
        if (Steps is null || Steps.Any(step => step is null)
            || Steps.Count != controls.Length
            || Steps.Select(step => step.Code).Distinct(StringComparer.Ordinal).Count() != Steps.Count
            || controls.Any(kind => Steps.Count(step => step.Kind == kind) != 1)
            || !Steps.Any(step => step.Code == InitialStep && step.Kind == validation.InitialControlKind)
            || !Steps.Any(step => step.Code == RelatedCaseInitialStep && step.Kind == validation.RelatedCaseControlKind)
            || !Steps.Any(step => step.Code == ReviewStep && step.Kind == validation.ReviewControlKind)
            || Steps.Any(step => string.IsNullOrWhiteSpace(step.Code)
                || step.Code.Length > validation.MaximumKeyLength
                || string.IsNullOrWhiteSpace(step.Prompt)
                || step.Prompt.Length > validation.MaximumPromptLength
                || string.IsNullOrWhiteSpace(step.Phase)
                || step.Phase.Length > validation.MaximumPhaseLength
                || step.CompletionField is not null && !validation.CompletionFields.Contains(step.CompletionField)
                || step.ConcernFlag is not null && !validation.ConcernFlags.Contains(step.ConcernFlag)))
        {
            throw new ArgumentException("The enquiry flow is incomplete or uses unsupported controls.");
        }
    }
}

public class EnquiryStepOptions
{
    public string Code { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string? CompletionField { get; set; }
    public string? ConcernFlag { get; set; }
}
