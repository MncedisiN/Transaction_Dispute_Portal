using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Api.Models.Requests;

public class UpdateUserRequest
{
    [Required]
    public string Role { get; set; } = string.Empty;

    [Required]
    public string Status { get; set; } = string.Empty;

    [Range(1, 50)]
    public int Capacity { get; set; }

    [MaxLength(100)]
    public string? FirstName { get; set; }

    [MaxLength(100)]
    public string? LastName { get; set; }

    [MaxLength(100)]
    public string? MiddleName { get; set; }

    public UpdateUserRequest()
    {
    }

    public UpdateUserRequest(
        string Role,
        string Status,
        int Capacity,
        string? FirstName = null,
        string? LastName = null,
        string? MiddleName = null)
    {
        this.Role = Role;
        this.Status = Status;
        this.Capacity = Capacity;
        this.FirstName = FirstName;
        this.LastName = LastName;
        this.MiddleName = MiddleName;
    }
}
