namespace DisputePortal.Api.Models.Responses;

public class CaseStatusCatalogue
{
    public long Revision { get; set; }
    public CaseStatusDefinition[] Items { get; set; } = [];
}
