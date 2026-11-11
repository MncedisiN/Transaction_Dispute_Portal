using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Platform.Infrastructure.Models;

public class ReplayDeadLetters
{
    public int Maximum { get; set; }

    [Required]
    public string Reason { get; set; } = string.Empty;

    public ReplayDeadLetters()
    {
    }

    public ReplayDeadLetters(int Maximum, string Reason)
    {
        this.Maximum = Maximum;
        this.Reason = Reason;
    }
}
