using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Platform.Configuration; 
public static class ConfigurationBootstrapSchema
{
    public static readonly string PostgreSql = SqlScripts.ReadSqlScript("PostgreSql/Schema/Configuration/ConfigurationBootstrapSchema/CreateConfigurationServiceSchema");
    public static readonly string SqlServer = SqlScripts.ReadSqlScript("SqlServer/Schema/Configuration/ConfigurationBootstrapSchema/CreateConfigurationServiceSchema");
}
