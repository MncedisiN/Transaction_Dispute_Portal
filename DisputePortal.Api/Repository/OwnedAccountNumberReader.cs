using System.Data.Common;
using System.Security.Cryptography;
using Dapper;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Security;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;

internal class OwnedAccountNumberReader(
    IDatabaseDialect dialect,
    ISecretProtector protector,
    IOtpHasher hasher,
    Func<DbConnection, DbTransaction, CancellationToken, Task> lockOperations,
    Func<DbConnection, DbTransaction, Guid, CancellationToken, Task> recordAllocation)
{
    // A supplied transaction must already hold the shared operations lock.
    public async Task<IReadOnlyDictionary<Guid, string?>> GetOwnedAccountNumbers(
        DbConnection connection,
        DbTransaction? transaction,
        Guid owner,
        IEnumerable<Guid> accountIds,
        CancellationToken ct,
        bool activeOnly = false)
    {
        var ids = accountIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, string?>();
        }

        var rows = await Rows(connection, transaction, owner, ids, activeOnly, ct);
        if (transaction is null && rows.Any(CanAllocate))
        {
            await using var ownedTransaction = await connection.BeginTransactionAsync(ct);
            await lockOperations(connection, ownedTransaction, ct);
            // Another owned read/reveal may have assigned the number while we waited.
            rows = await Rows(connection, ownedTransaction, owner, ids, activeOnly, ct);
            var result = await ResolveOwnedAccountNumber(connection, ownedTransaction, owner, rows, ct);
            await ownedTransaction.CommitAsync(ct);
            return result;
        }

        return await ResolveOwnedAccountNumber(connection, transaction, owner, rows, ct);
    }

    private async Task<NumberRow[]> Rows(
        DbConnection connection,
        DbTransaction? transaction,
        Guid owner,
        Guid[] ids,
        bool activeOnly,
        CancellationToken ct)
    {
        var local = dialect.ExistsAsBoolean(SqlScripts.ReadSqlScript("Shared/Queries/Api/OwnedAccountNumberReader/CheckLocalBankingAccounts"));
        var sql = SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/OwnedAccountNumberReader/GetActiveAccounts",
            ("Local", local),
            ("MembershipPredicate", dialect.InListExpression("a.\"publicId\"", "@Ids")));
        return (await connection.QueryAsync<NumberRow>(new CommandDefinition(
            sql,
            new
            {
                Owner = owner,
                Ids = ids,
                ActiveOnly = activeOnly ? 1 : 0
            },
            transaction,
            cancellationToken: ct))).ToArray();
    }

    private static string Suffix(NumberRow row) => new(row.Masked.Where(char.IsAsciiDigit).ToArray());

    private static bool CanAllocate(NumberRow row) =>
        row.ProtectedNumber is null && row.IsLocal && row.Status == "Active" && Suffix(row).Length >= 4;

    private async Task<IReadOnlyDictionary<Guid, string?>> ResolveOwnedAccountNumber(
        DbConnection connection,
        DbTransaction? transaction,
        Guid owner,
        NumberRow[] rows,
        CancellationToken ct)
    {
        var result = new Dictionary<Guid, string?>();
        foreach (var row in rows)
        {
            string? number = null;
            if (row.ProtectedNumber is not null)
            {
                var stored = protector.Unprotect(row.ProtectedNumber);
                // A masked suffix must never be represented as a full account number.
                if (stored.Length >= 5 && stored.All(char.IsAsciiDigit))
                {
                    number = stored;
                }
            }
            else if (transaction is not null && CanAllocate(row))
            {
                var suffix = Suffix(row)[^4..];
                string hash;
                do
                {
                    number = RandomNumberGenerator.GetInt32(100000, 1000000).ToString() + suffix;
                    hash = hasher.Hash(Guid.Empty, "account-number:" + number);
                }
                while (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    """
                    SELECT
                        COUNT(*)
                    FROM "accounts"."accounts" AS "customer_accounts"
                    WHERE "customer_accounts"."numberHash" = @Hash
                    """,
                    new { Hash = hash },
                    transaction,
                    cancellationToken: ct)) > 0);
                var changed = await connection.ExecuteAsync(new CommandDefinition(
                    SqlScripts.ReadSqlScript("Shared/Queries/Api/OwnedAccountNumberReader/UpdateAccountsForAccount"),
                    new
                    {
                        Protected = protector.Protect(number),
                        Hash = hash,
                        Account = row.Id,
                        Owner = owner
                    },
                    transaction,
                    cancellationToken: ct));
                if (changed != 1)
                {
                    throw new PortalConflictException("Account details changed. Please refresh and try again.");
                }

                await recordAllocation(connection, transaction, row.Id, ct);
            }

            result[row.Id] = number;
        }

        return result;
    }

    private class NumberRow
    {
        public Guid Id { get; set; }
        public string Masked { get; set; } = string.Empty;
        public string? ProtectedNumber { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool IsLocal { get; set; }

        public NumberRow()
        {
        }

        public NumberRow(Guid Id, string Masked, string? ProtectedNumber, string Status, bool IsLocal)
        {
            this.Id = Id;
            this.Masked = Masked;
            this.ProtectedNumber = ProtectedNumber;
            this.Status = Status;
            this.IsLocal = IsLocal;
        }
    }
}
