namespace DisputePortal.Api.Models.Responses;

public class LoginDeviceDto
{
    public Guid Id { get; set; }
    public string DeviceType { get; set; } = string.Empty;
    public string Browser { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }
    public bool Current { get; set; }

    public LoginDeviceDto()
    {
    }

    public LoginDeviceDto(
        Guid Id,
        string DeviceType,
        string Browser,
        DateTimeOffset CreatedAt,
        DateTimeOffset ExpiresAt,
        DateTimeOffset LastUsedAt,
        bool Current)
    {
        this.Id = Id;
        this.DeviceType = DeviceType;
        this.Browser = Browser;
        this.CreatedAt = CreatedAt;
        this.ExpiresAt = ExpiresAt;
        this.LastUsedAt = LastUsedAt;
        this.Current = Current;
    }
}
