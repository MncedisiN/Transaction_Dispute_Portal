namespace DisputePortal.Api.Interfaces.Banking;

public interface ICustomerTransactionPresentation
{
    string TypeLabel(string type, string? configuredLabel = null);

    string Description(string type, string? merchant, string? description);

    Task<ICustomerTransactionPresentation> LoadTransactionPresentationRules(CancellationToken ct) => Task.FromResult(this);
}
