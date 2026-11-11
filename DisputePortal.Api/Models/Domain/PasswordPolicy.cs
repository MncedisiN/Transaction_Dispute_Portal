namespace DisputePortal.Api.Models.Domain;

public static class PasswordPolicy
{
    private static readonly string[] Common = ["password", "qwerty", "letmein", "welcome123", "123456789"];

    public static bool IsStrong(string? value) => value is { Length: >= 12 and <= 128 }
        && value.Any(char.IsUpper)
        && value.Any(char.IsLower)
        && value.Any(char.IsDigit)
        && value.Any(c => !char.IsLetterOrDigit(c))
        && !Common.Any(x => value.Contains(x, StringComparison.OrdinalIgnoreCase));
}
