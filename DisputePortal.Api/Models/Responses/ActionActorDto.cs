using System.Text.Json.Serialization;

namespace DisputePortal.Api.Models.Responses;

public class ActionActorDto
{
    public Guid? UserId { get; set; }
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? AccountName { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = [];
    public string Kind { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Provenance { get; set; }
    public string? LoginIdentifier { get; set; }

    public ActionActorDto()
    {
    }

    public ActionActorDto(
        Guid? UserId,
        string? FirstName,
        string? MiddleName,
        string? LastName,
        string? AccountName,
        IReadOnlyList<string> Roles,
        string Kind,
        string? Provenance,
        string? LoginIdentifier = null)
    {
        this.UserId = UserId;
        this.FirstName = FirstName;
        this.MiddleName = MiddleName;
        this.LastName = LastName;
        this.AccountName = AccountName;
        this.Roles = Roles;
        this.Kind = Kind;
        this.Provenance = Provenance;
        this.LoginIdentifier = LoginIdentifier;
    }

    public ActionActorDto(ActionActorDto other)
    {
        UserId = other.UserId;
        FirstName = other.FirstName;
        MiddleName = other.MiddleName;
        LastName = other.LastName;
        AccountName = other.AccountName;
        Roles = other.Roles;
        Kind = other.Kind;
        Provenance = other.Provenance;
        LoginIdentifier = other.LoginIdentifier;
    }

    public string? FullName => !string.IsNullOrWhiteSpace(FirstName) && !string.IsNullOrWhiteSpace(LastName) ? string.Join(
        " ",
        new[] { FirstName, MiddleName, LastName }.Where(value => !string.IsNullOrWhiteSpace(value))) : null;
    public string Label => FullName ?? AccountName ?? "Actor not recorded";
}
