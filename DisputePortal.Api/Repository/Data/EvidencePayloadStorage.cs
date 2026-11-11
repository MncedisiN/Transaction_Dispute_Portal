using System.Data;
using System.Data.Common;
using Dapper;
using Microsoft.Data.SqlClient;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;

internal static class EvidencePayloadStorage
{
    public static async Task InsertEncryptedEvidencePayload(
        DbConnection connection,
        DbTransaction transaction,
        Guid id,
        byte[] content,
        byte[]? original,
        int writeTimeoutSeconds,
        CancellationToken ct)
    {
        if (connection is SqlConnection sql && transaction is SqlTransaction sqlTransaction)
        {
            // These bounded, encrypted arrays already exist in memory. Bind them directly:
            // wrapping them in streams makes SqlClient's large-value upload path much slower.
            // Keep the ordinary single-row INSERT and the caller's transaction.
            await using var command = new SqlCommand(
                """
                INSERT INTO "disputes"."evidencePayloads"(
                    "publicId",
                    "protectedContent",
                    "protectedOriginal"
                )
                VALUES (
                    @Id,
                    @Content,
                    @Original
                )
                """,
                sql,
                sqlTransaction)
            {
                CommandTimeout = writeTimeoutSeconds
            };
            command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
            command.Parameters.Add("@Content", SqlDbType.VarBinary, -1).Value = content;
            command.Parameters.Add("@Original", SqlDbType.VarBinary, -1).Value = (object?)original ?? DBNull.Value;
            await command.ExecuteNonQueryAsync(ct);
            return;
        }

        using var primaryParameter = new ProtectedBinaryParameter(content);
        using var originalParameter = original is null ? null : new ProtectedBinaryParameter(original);
        if (originalParameter is null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO "disputes"."evidencePayloads"(
                    "publicId",
                    "protectedContent"
                )
                VALUES (
                    @Id,
                    @Content
                )
                """,
                new { Id = id, Content = primaryParameter },
                transaction,
                commandTimeout: writeTimeoutSeconds,
                cancellationToken: ct));
        }
        else
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO "disputes"."evidencePayloads"(
                    "publicId",
                    "protectedContent",
                    "protectedOriginal"
                )
                VALUES (
                    @Id,
                    @Content,
                    @Original
                )
                """,
                new
                {
                    Id = id,
                    Content = primaryParameter,
                    Original = originalParameter
                },
                transaction,
                cancellationToken: ct));
        }
    }
}
