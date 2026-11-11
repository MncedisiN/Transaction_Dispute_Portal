using Microsoft.Data.SqlClient;
using Npgsql;

namespace DisputePortal.Api.Repository.Data;

public class DatabaseConfiguration
{
    private DatabaseConfiguration(DatabaseProvider provider, string connectionString)
    {
        Provider = provider;
        ConnectionString = connectionString;
    }

    public DatabaseProvider Provider { get; }
    public string ConnectionString { get; }

    public static DatabaseConfiguration ResolveDatabaseConfiguration(IConfiguration configuration)
    {
        var provider = DatabaseDialect.ParseProvider(configuration["Database:Provider"]);
        var connectionName = provider == DatabaseProvider.PostgreSql ? "PostgreSql" : "SqlServer";
        var connectionString = configuration.GetConnectionString(connectionName) ?? configuration.GetConnectionString("Portal");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return new DatabaseConfiguration(provider, connectionString);
        }

        var host = configuration["Database:Host"];
        var database = configuration["Database:Name"];
        var username = configuration["Database:Username"];
        var password = configuration["Database:Password"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database))
        {
            throw new InvalidOperationException("Configure the selected ConnectionStrings entry or Database host and name values.");
        }

        try
        {
            if (provider == DatabaseProvider.PostgreSql)
            {
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    throw new InvalidOperationException("PostgreSQL requires Database:Username and Database:Password when no connection string is configured.");
                }

                var port = int.TryParse(configuration["Database:Port"], out var configuredPort) && configuredPort > 0 ? configuredPort : 5432;
                var builder = new NpgsqlConnectionStringBuilder
                {
                    Host = host,
                    Port = port,
                    Database = database,
                    Username = username,
                    Password = password,
                    Timeout = 5,
                    CommandTimeout = 15,
                    Timezone = "UTC"
                };
                builder["SSL Mode"] = configuration["Database:SslMode"] ?? "VerifyFull";
                return new DatabaseConfiguration(provider, builder.ConnectionString);
            }

            var sql = new SqlConnectionStringBuilder
            {
                DataSource = host,
                InitialCatalog = database,
                ConnectTimeout = 5,
                TrustServerCertificate = string.Equals(
                configuration["Database:TrustServerCertificate"],
                "true",
                StringComparison.OrdinalIgnoreCase),
                Encrypt = SqlConnectionEncryptOption.Mandatory,
                IntegratedSecurity = string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(password)
            };
            if (!sql.IntegratedSecurity)
            {
                sql.UserID = username;
                sql.Password = password;
            }

            return new DatabaseConfiguration(provider, sql.ConnectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("The selected database connection configuration is invalid.");
        }
    }

    public void ValidateProduction() => DisputePortal.Platform.Infrastructure.TransportSecurity.ValidateDatabase(
        Provider.ToString(),
        ConnectionString);
}
