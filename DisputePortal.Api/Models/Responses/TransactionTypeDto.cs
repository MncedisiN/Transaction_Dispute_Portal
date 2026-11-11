namespace DisputePortal.Api.Models.Responses;

public class TransactionTypeDto
{
    public string TypeKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    public TransactionTypeDto()
    {
    }

    public TransactionTypeDto(string TypeKey, string Label)
    {
        this.TypeKey = TypeKey;
        this.Label = Label;
    }
}
