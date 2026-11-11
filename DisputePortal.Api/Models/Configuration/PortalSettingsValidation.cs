namespace DisputePortal.Api.Models.Configuration;

public class PortalSettingsValidation
{
    public int MaximumSettingsBytes { get; set; }
    public int MaximumLabelLength { get; set; }
    public int MaximumDescriptionLength { get; set; }
    public int MaximumKeyLength { get; set; }
    public int MaximumPromptLength { get; set; }
    public int MaximumPhaseLength { get; set; }
    public string InitialControlKind { get; set; } = string.Empty;
    public string RelatedCaseControlKind { get; set; } = string.Empty;
    public string ReviewControlKind { get; set; } = string.Empty;
    public string[] EnquiryControlKinds { get; set; } = [];
    public string[] CompletionFields { get; set; } = [];
    public string[] ConcernFlags { get; set; } = [];
    public Dictionary<string, NumericSettingRule> ClientOptions { get; set; } = [];
}

public class NumericSettingRule
{
    public int Minimum { get; set; }
    public int Maximum { get; set; }
}
