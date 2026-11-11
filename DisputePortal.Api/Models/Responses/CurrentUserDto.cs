namespace DisputePortal.Api.Models.Responses;

public class CurrentUserDto
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string MaskedEmail { get; set; } = string.Empty;
    public IReadOnlyList<string> Roles { get; set; } = [];
    public IReadOnlyList<string> Permissions { get; set; } = [];

    public CurrentUserDto()
    {
    }

    public CurrentUserDto(Guid Id, string DisplayName, string MaskedEmail, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions)
    {
        this.Id = Id;
        this.DisplayName = DisplayName;
        this.MaskedEmail = MaskedEmail;
        this.Roles = Roles;
        this.Permissions = Permissions;
    }
}
