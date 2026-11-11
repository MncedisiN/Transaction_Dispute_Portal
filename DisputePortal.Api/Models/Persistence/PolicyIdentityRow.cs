namespace DisputePortal.Api.Models.Persistence;

internal class PolicyIdentityRow
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public int LastVersion { get; set; }
    public long ConcurrencyToken { get; set; }
    public string Scope { get; set; } = "";
}
