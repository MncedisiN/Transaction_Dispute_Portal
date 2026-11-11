using DisputePortal.Api.Configuration;
using DisputePortal.Api.Models.Exceptions;

namespace DisputePortal.Api.Models.Configuration;

public class PortalReferenceSettings
{
    public bool CustomerSearchEnabled { get; set; }
    public bool CustomerAccountOpeningEnabled { get; set; }
    public Dictionary<string, RoleDescription> Roles { get; set; } = [];
    public Dictionary<string, QueueDescription> Queues { get; set; } = [];
    public Dictionary<string, string> Priorities { get; set; } = [];
    public string[] Severities { get; set; } = [];
    public Dictionary<string, string> TransactionStatuses { get; set; } = [];
    public Dictionary<string, string> PaymentStatuses { get; set; } = [];
    public string[] Availability { get; set; } = [];
    public string[] StaffStatuses { get; set; } = [];
    public EnquiryFlowOptions EnquiryFlow { get; set; } = new();
    public Dictionary<string, int> ClientOptions { get; set; } = [];

    public void Validate(PortalSettingsValidation validation)
    {
        if (EnquiryFlow is null)
        {
            throw new PortalValidationException("The enquiry flow is required.");
        }

        try
        {
            EnquiryFlow.Validate(validation);
        }
        catch (ArgumentException)
        {
            throw new PortalValidationException("The enquiry flow is incomplete or uses unsupported controls.");
        }
        if (Roles is null || Queues is null
            || Roles.Values.Any(role => role is null || string.IsNullOrWhiteSpace(role.Label)
                || role.Label.Length > validation.MaximumLabelLength || role.Description is null
                || role.Description.Length > validation.MaximumDescriptionLength)
            || Queues.Values.Any(queue => queue is null || string.IsNullOrWhiteSpace(queue.Label)
                || queue.Label.Length > validation.MaximumLabelLength || queue.Steps is null
                || queue.Steps.Any(step => string.IsNullOrWhiteSpace(step) || step.Length > validation.MaximumDescriptionLength))
            || Priorities is null || Priorities.Count == 0 || Severities is null || Severities.Length == 0
            || ClientOptions is null || validation.ClientOptions.Count == 0
            || validation.ClientOptions.Any(rule => !ClientOptions.TryGetValue(rule.Key, out var value)
                || value < rule.Value.Minimum || value > rule.Value.Maximum))
        {
            throw new PortalValidationException("Portal reference descriptions are incomplete or invalid.");
        }
    }
}

public class RoleDescription
{
    public int Rank { get; set; } = int.MaxValue;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class QueueDescription
{
    public string Label { get; set; } = string.Empty;
    public string[] Steps { get; set; } = [];
}
