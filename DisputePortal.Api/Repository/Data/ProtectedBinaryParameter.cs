using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DisputePortal.Api.Repository; 
internal sealed class ProtectedBinaryParameter(byte[] content) : SqlMapper.ICustomQueryParameter, IDisposable
{
    private MemoryStream? stream;

    public void AddParameter(IDbCommand command, string name)
    {
        if (command is SqlCommand sql)
        {
            stream?.Dispose();
            stream = new MemoryStream(content, writable: false);
            sql.Parameters.Add(new SqlParameter(name, SqlDbType.VarBinary, -1) { Value = stream });
        }
        else
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.DbType = DbType.Binary;
            parameter.Value = content;
            parameter.Size = content.Length;
            command.Parameters.Add(parameter);
        }
    }

    public void Dispose() => stream?.Dispose();
}
