using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class SaveUserRequest
{
    [Required, EmailAddress, MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = string.Empty;

    [Required, MinLength(12), MaxLength(128)]
    public string Password { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string? FirstName { get; set; }

    [Required, MaxLength(100)]
    public string? LastName { get; set; }

    [MaxLength(100)]
    public string? MiddleName { get; set; }

    public SaveUserRequest()
    {
    }

    public SaveUserRequest(
        string Email,
        string DisplayName,
        string Role,
        string Password,
        string? FirstName = null,
        string? LastName = null,
        string? MiddleName = null)
    {
        this.Email = Email;
        this.DisplayName = DisplayName;
        this.Role = Role;
        this.Password = Password;
        this.FirstName = FirstName;
        this.LastName = LastName;
        this.MiddleName = MiddleName;
    }

    public SaveUserRequest(SaveUserRequest other)
    {
        Email = other.Email;
        DisplayName = other.DisplayName;
        Role = other.Role;
        Password = other.Password;
        FirstName = other.FirstName;
        LastName = other.LastName;
        MiddleName = other.MiddleName;
    }
}
