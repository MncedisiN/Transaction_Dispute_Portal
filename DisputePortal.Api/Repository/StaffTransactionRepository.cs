using DisputePortal.Api.Models.Requests;
using Dapper;
using System.Text.Json;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Security;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;
// Customer-assistance reads are separate from the ownership-only customer repository.
public class StaffTransactionRepository(
    IDatabaseConnectionFactory factory,
    IDatabaseDialect databaseDialect,
    TimeZoneInfo businessTimeZone,
    ISecretProtector protector) : RepositoryBase(factory, databaseDialect), IStaffTransactionRepository
{
    private static readonly string From = SqlScripts.ReadSqlScript("Shared/Queries/Api/StaffTransactionRepository/JoinStaffTransactionDetails");
    private string Select => SqlScripts.RenderSqlScript(
        "Shared/Queries/Api/StaffTransactionRepository/SelectStaffTransactionDetailsWithDisputeAndRefund",
        ("EligibleTransactionExpression", dialect.EligibleTransactionExpression));

    public async Task<PageResult<StaffTransactionDto>> Search(
        Guid actor,
        int page,
        int pageSize,
        string? search,
        string? status,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct)
    {
        if (page < 1
            || pageSize is < 1 or > 100
            || search?.Length > 100
            || status is not (null or "Posted" or "Pending" or "Reversed"))
        {
            throw new PortalValidationException("Check the transaction filters.");
        }

        var dates = new HistoryFilter
        {
            Status = null,
            From = from,
            To = to
        };
        dates.ValidateHistoryFilter();
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await RequireAssistanceStaff(c, tx, actor, ct);
        var where = SqlScripts.RenderSqlScript("Shared/Queries/Api/StaffTransactionRepository/FilterUnarchivedStaffTransactionsByDateStatusAndSearch", ("Join", string.Join(
                " OR ",
                new[] { "p.\"displayName\"", "u.\"loginIdentifier\"", "p.email", "p.phone", "t.\"transactionReference\"", SqlScripts.ReadSqlScript("Shared/Queries/Api/StaffTransactionRepository/SelectStaffTransactionColumnsWithCustomer"), "t.\"merchantName\"", "a.\"maskedAccountNumber\"" }.Select(column => dialect.LikeExpression(
            column,
            "@Search")))));
        using var results = await c.QueryMultipleAsync(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/StaffTransactionRepository/GetStaffTransactionPage",
            ("SelectQuery", Select),
            ("FilterClause", where),
            ("PaginationClause", dialect.PaginationClause),
            ("RelatedFilterClause", where)),
            new
            {
                From = dates.StartUtc(businessTimeZone),
                To = dates.EndUtc(businessTimeZone),
                Status = status,
                Search = CleanSearch(search)?.ToLowerInvariant(),
                Offset = PageOffset(page, pageSize),
                PageSize = pageSize
            },
            tx,
            cancellationToken: ct));
        var items = (await results.ReadAsync<StaffTransactionDto>()).ToArray();
        var total = await results.ReadSingleAsync<long>();
        await AuditOperation(
            c,
            tx,
            actor,
            "AssistedTransactionsSearched",
            actor,
            new
            {
                Page = page,
                PageSize = pageSize,
                Returned = items.Length
            },
            ct);
        await tx.CommitAsync(ct);
        return new(items, page, pageSize, total);
    }

    public async Task<StaffTransactionDetailDto> GetStaffTransactionDetails(Guid actor, Guid transaction, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await RequireAssistanceStaff(c, tx, actor, ct);
        var item = await c.QuerySingleOrDefaultAsync<StaffTransactionDto>(new CommandDefinition(
            SqlScripts.RenderSqlScript("Shared/Queries/Api/StaffTransactionRepository/GetStaffTransactionById", ("SelectQuery", Select)),
            new { Id = transaction },
            tx,
            cancellationToken: ct)) ?? throw new PortalNotFoundException();
        var person = await c.QuerySingleAsync<PrivateCustomer>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/StaffTransactionRepository/GetUser",
            ("DateTimeProjection", dialect.DateTimeProjection("p.\"dateOfBirth\""))),
            new { Id = item.CustomerId },
            tx,
            cancellationToken: ct));
        var account = await c.QuerySingleAsync<PrivateAccount>(new CommandDefinition(
            """
            SELECT
                "customer_accounts"."protectedNumber" AS ProtectedNumber,
                "customer_accounts"."accountStatus" AS AccountStatus
            FROM "accounts"."accounts" AS "customer_accounts"
            WHERE "customer_accounts"."publicId" = @Id
            """,
            new { Id = item.AccountId },
            tx,
            cancellationToken: ct));
        var customer = new AssistedCustomerDto(
            person.Id,
            person.DisplayName,
            person.FirstName,
            person.MiddleName,
            person.LastName,
            person.Email,
            person.Phone,
            person.ProtectedIdentity is null ? null : protector.Unprotect(person.ProtectedIdentity),
            person.DateOfBirth,
            person.ProtectedAddress is null ? null : JsonSerializer.Deserialize<ResidentialAddress>(protector.Unprotect(person.ProtectedAddress)),
            person.Status,
            person.RegisteredAt);
        var number = account.ProtectedNumber is null ? null : protector.Unprotect(account.ProtectedNumber);
        await AuditOperation(
            c,
            tx,
            actor,
            "AssistedCustomerDetailsViewed",
            transaction,
            new { CustomerId = item.CustomerId, AccountId = item.AccountId },
            ct);
        await tx.CommitAsync(ct);
        return new(item, customer, number, account.AccountStatus);
    }

    private class PrivateCustomer
    {
        public Guid Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string? FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? ProtectedIdentity { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? ProtectedAddress { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTimeOffset RegisteredAt { get; set; }

        public PrivateCustomer()
        {
        }

        public PrivateCustomer(
            Guid Id,
            string DisplayName,
            string? FirstName,
            string? MiddleName,
            string? LastName,
            string? Email,
            string? Phone,
            string? ProtectedIdentity,
            DateTime? DateOfBirth,
            string? ProtectedAddress,
            string Status,
            DateTimeOffset RegisteredAt)
        {
            this.Id = Id;
            this.DisplayName = DisplayName;
            this.FirstName = FirstName;
            this.MiddleName = MiddleName;
            this.LastName = LastName;
            this.Email = Email;
            this.Phone = Phone;
            this.ProtectedIdentity = ProtectedIdentity;
            this.DateOfBirth = DateOfBirth;
            this.ProtectedAddress = ProtectedAddress;
            this.Status = Status;
            this.RegisteredAt = RegisteredAt;
        }
    }

    private class PrivateAccount
    {
        public string? ProtectedNumber { get; set; }
        public string AccountStatus { get; set; } = string.Empty;

        public PrivateAccount()
        {
        }

        public PrivateAccount(string? ProtectedNumber, string AccountStatus)
        {
            this.ProtectedNumber = ProtectedNumber;
            this.AccountStatus = AccountStatus;
        }
    }
}
