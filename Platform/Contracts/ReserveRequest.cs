using System.Text.Json.Serialization;

namespace DisputePortal.Platform.Contracts;

public class ReserveRequest
{
    public Guid TransactionId { get; set; }
    public Guid UserId { get; set; }
    public Guid AccountId { get; set; }
    public decimal Amount { get; set; }
    public decimal Fee { get; set; }
    public string Reference { get; set; } = null!;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int SettingsVersion { get; set; } = 0;
}
