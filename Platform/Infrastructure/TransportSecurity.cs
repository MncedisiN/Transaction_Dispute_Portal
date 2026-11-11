using System.Security.Authentication;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Data.SqlClient;
using Npgsql;
using Microsoft.Extensions.Configuration;

namespace DisputePortal.Platform.Infrastructure;
// One native transport policy shared by the API, financial services and HTTP clients.
public static class TransportSecurity
{
    public const SslProtocols Protocols = SslProtocols.Tls12 | SslProtocols.Tls13;

    public static void ConfigureHttps(KestrelServerOptions server) =>
        server.ConfigureHttpsDefaults(https => https.SslProtocols = Protocols);

    public static HttpClientHandler HttpHandler() => new()
    {
        AllowAutoRedirect = false,
        SslProtocols = Protocols
    };

    public static void ValidateDatabase(string provider, string connectionString)
    {
        try
        {
            if (provider == "SqlServer")
            {
                var sql = new SqlConnectionStringBuilder(connectionString);
                if (string.IsNullOrWhiteSpace(sql.DataSource)
                    || string.IsNullOrWhiteSpace(sql.InitialCatalog)
                    || sql.TrustServerCertificate
                    || sql.Encrypt == SqlConnectionEncryptOption.Optional)
                {
                    throw new InvalidOperationException("Production SQL Server requires a named database, encryption and certificate validation.");
                }
            }
            else if (provider == "PostgreSql")
            {
                var pg = new NpgsqlConnectionStringBuilder(connectionString);
                if (string.IsNullOrWhiteSpace(pg.Host) || string.IsNullOrWhiteSpace(pg.Database) || pg.SslMode != SslMode.VerifyFull)
                {
                    throw new InvalidOperationException("Production PostgreSQL requires a named database and SSL Mode=VerifyFull.");
                }
            }
            else
            {
                throw new InvalidOperationException("The database provider is not supported.");
            }
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("The database transport configuration is invalid.");
        }
    }

    public static Uri? MonitoringEndpoint(IConfiguration configuration, bool production)
    {
        if (!configuration.GetValue<bool>("OpenTelemetry:ExportEnabled"))
        {
            return null;
        }

        var value = configuration["OpenTelemetry:OtlpEndpoint"];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Enabled telemetry export requires an OTLP endpoint.");
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https")
            || production
            && endpoint.Scheme != "https"
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new InvalidOperationException("Configure a valid telemetry endpoint; production telemetry requires HTTPS.");
        }

        return endpoint;
    }

    public static void ValidateServerConfiguration(IConfiguration configuration)
    {
        foreach (var entry in configuration.GetSection("Kestrel").AsEnumerable())
        {
            if (entry.Value is null)
            {
                continue;
            }

            if (entry.Key.EndsWith(":AllowInvalid", StringComparison.OrdinalIgnoreCase)
                && bool.TryParse(entry.Value, out var invalid)
                && invalid)
            {
                throw new InvalidOperationException("Production TLS certificates cannot enable AllowInvalid.");
            }

            if (entry.Key.Contains(":SslProtocols", StringComparison.OrdinalIgnoreCase)
                && (!Enum.TryParse<SslProtocols>(entry.Value, true, out var selected)
                || selected == SslProtocols.None
                || (selected & ~Protocols) != 0))
            {
                throw new InvalidOperationException("Production HTTPS endpoint protocols must be TLS 1.2 or TLS 1.3.");
            }
        }
    }

    public static void ValidateServices(ServiceOptions options)
    {
        ValidateDatabase(options.DatabaseProvider, options.ConnectionString);
        if (options.BrokerEnabled
            && (!options.BrokerTls
            || string.IsNullOrWhiteSpace(options.BrokerUser)
            || options.BrokerUser == "guest"
            || string.IsNullOrWhiteSpace(options.BrokerPassword)
            || string.IsNullOrWhiteSpace(options.BrokerVirtualHost)
            || options.BrokerVirtualHost == "/")
            || options.Endpoints.Values.Any(value => !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)))
        {
            throw new InvalidOperationException("Production service traffic requires validated HTTPS endpoints and dedicated TLS broker credentials.");
        }
    }
}
