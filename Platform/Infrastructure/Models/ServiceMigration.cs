using System.Security.Cryptography;
using System.Text;

namespace DisputePortal.Platform.Infrastructure.Models;
// Immutable release instructions, not customer-editable configuration or DTOs.
public class ServiceMigration(string version, string sql)
{
    public string Version { get; } = version;
    public string Sql { get; } = sql;
    public string Checksum { get; } = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
}
