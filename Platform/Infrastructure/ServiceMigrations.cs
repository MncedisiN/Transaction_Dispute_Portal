using DisputePortal.Platform.Infrastructure.Models;

namespace DisputePortal.Platform.Infrastructure;

public static class ServiceMigrations
{
    private static readonly HashSet<string> Services = ["configuration", "accounts", "ledger", "transactions", "payments", "notifications", "audit", "reporting", "scheduling", "debt"];

    public static IReadOnlyList<ServiceMigration> GetServiceMigrations(string service, string provider)
    {
        if (!Services.Contains(service))
        {
            return [];
        }
         
        if (provider == "SqlServer" && service != "configuration")
        {
            return [];
        }

        if (provider is not ("PostgreSql" or "SqlServer"))
        {
            throw new InvalidOperationException("Unknown migration provider.");
        }

        var prefix = "DatabaseSql/" + provider + "/Initialization/" + service + "/";
        var versions = typeof(ServiceMigrations).Assembly.GetManifestResourceNames()
            .Select(name => name.Replace('\\', '/'))
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal)
                && name.EndsWith(".sql", StringComparison.Ordinal))
            .Select(name => Path.GetFileNameWithoutExtension(name))
            .Where(name => name.Length > 0 && char.IsDigit(name[0]))
            .OrderBy(name => name, StringComparer.Ordinal);
        return versions.Select(version => ReadMigrationResource(service, provider, version)).ToArray();
    }

    private static ServiceMigration ReadMigrationResource(string service, string provider, string version)
    {
        var resource = "DatabaseSql/" + provider + "/Initialization/" + service + "/" + version + ".sql";
        var assembly = typeof(ServiceMigrations).Assembly;
        var name = assembly.GetManifestResourceNames().SingleOrDefault(value => value.Replace('\\', '/') == resource) ?? throw new InvalidOperationException("The release is missing a required service migration.");
        using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("The release is missing a required service migration.");
        using var reader = new StreamReader(stream);
        return new ServiceMigration(version, reader.ReadToEnd());
    }
}
