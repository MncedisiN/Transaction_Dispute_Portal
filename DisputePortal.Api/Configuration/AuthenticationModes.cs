namespace DisputePortal.Api.Configuration;

public static class AuthenticationModes
{
    public const string Cookie = "Cookie";
    public const string Bearer = "Bearer";
    public const string Oidc = "Oidc";

    public static bool UsesBearerTokens(string? mode) => mode?.Equals(Bearer, StringComparison.OrdinalIgnoreCase) == true
        || mode?.Equals(Oidc, StringComparison.OrdinalIgnoreCase) == true;
}
