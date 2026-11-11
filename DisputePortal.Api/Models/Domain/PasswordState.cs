namespace DisputePortal.Api.Models.Domain;

public class PasswordState
{
    public string PasswordHash { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    public PasswordState()
    {
    }

    public PasswordState(string PasswordHash, string Status)
    {
        this.PasswordHash = PasswordHash;
        this.Status = Status;
    }
}
