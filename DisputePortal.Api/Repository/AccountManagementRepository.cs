using DisputePortal.Api.Configuration;
using Dapper;
using System.Security.Cryptography;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Security;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;

public class AccountManagementRepository(
    IDatabaseConnectionFactory factory,
    IDatabaseDialect databaseDialect,
    ISecretProtector secretProtector,
    IOtpHasher otpHasher,
    ApiRuntimeOptions runtime) : RepositoryBase(factory, databaseDialect), IAccountManagementRepository
{
    public async Task<AccountNumberDto> RevealAccountNumber(Guid user, Guid account, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        var numbers = await new OwnedAccountNumberReader(
            dialect,
            secretProtector,
            otpHasher,
            LockOperations,
            (connection, transaction, assignedAccount, token) => AuditOperation(
                connection,
                transaction,
                null,
                "LocalAccountNumberAssigned",
                assignedAccount,
                new { AccountId = assignedAccount, Source = "LocalAllocation" },
                token)).GetOwnedAccountNumbers(c, tx, user, [account], ct, activeOnly: true);
        if (!numbers.TryGetValue(account, out var number))
        {
            throw new PortalNotFoundException();
        }

        if (number is null)
        {
            throw new PortalConflictException("The full account number is not available from your account provider yet.");
        }

        await AuditOperation(c, tx, user, "AccountNumberRevealed", account, new { }, ct);
        await tx.CommitAsync(ct);
        return new(number);
    }

    public async Task<DeletionChallengeDto> ReserveDeletionChallenge(Guid user, string destination, string code, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        if (!(await Roles(c, tx, user, ct)).Contains("customer"))
        {
            throw new PortalForbiddenException();
        }

        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/AccountManagementRepository/CountAccountDeletionRequestsForUser"),
            new { User = user },
            tx,
            cancellationToken: ct)) > 0)
        {
            throw new PortalConflictException("Your account deletion request is already being reviewed.");
        }

        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/AccountManagementRepository/CountCustomerProfilesForUser",
            ("TrueValue", dialect.TrueLiteral),
            ("RelatedTrueValue", dialect.TrueLiteral)),
            new { User = user, Destination = destination },
            tx,
            cancellationToken: ct)) != 1)
        {
            throw new PortalConflictException("Use your verified email or mobile number.");
        }

        var now = DateTimeOffset.UtcNow;
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/AccountManagementRepository/CountAccountDeletionChallengesForUser"),
            new { User = user, Since = now.AddSeconds(-runtime.VerificationResendDelaySeconds) },
            tx,
            cancellationToken: ct)) > 0)
        {
            throw new PortalConflictException($"Wait {runtime.VerificationResendDelaySeconds} seconds before requesting another code.");
        }

        var id = Guid.NewGuid();
        var expires = now.AddMinutes(runtime.VerificationCodeLifetimeMinutes);
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/AccountManagementRepository/MarkAccountDeletionChallengesFailedForUser"),
            new
            {
                Id = id,
                User = user,
                Hash = otpHasher.Hash(id, code),
                Now = now,
                Expires = expires
            },
            tx,
            cancellationToken: ct));
        await tx.CommitAsync(ct);
        var masked = destination.Contains('@') ? destination[..1] + "••••@" + destination.Split('@')[1] : "••••" + destination[^4..];
        return new(id, masked, expires);
    }

    public async Task SetDeletionChallengeReady(Guid user, Guid challenge, bool ready, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/AccountManagementRepository/UpdateAccountDeletionChallengesForUser"),
            new
            {
                State = ready ? "Ready" : "Failed",
                Id = challenge,
                User = user
            },
            cancellationToken: ct));
    }

    public async Task<Guid> ConfirmDeletionRequest(Guid user, ConfirmDeletionRequest request, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        if (!(await Roles(c, tx, user, ct)).Contains("customer"))
        {
            throw new PortalForbiddenException();
        }

        var code = await c.QuerySingleOrDefaultAsync<DeletionCode>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/AccountManagementRepository/GetAccountDeletionChallengeForUser"),
            new { Id = request.ChallengeId, User = user },
            tx,
            cancellationToken: ct));
        if (code is null || code.State != "Ready" || code.Attempts >= 5 || code.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new PortalValidationException("This verification expired. Request a new code.");
        }

        if (!otpHasher.Matches(request.ChallengeId, request.Code, code.CodeHash))
        {
            await c.ExecuteAsync(new CommandDefinition(
                """
                UPDATE "identity"."accountDeletionChallenges"
                SET
                    "attempts" = "accountDeletionChallenges"."attempts" + 1
                WHERE "accountDeletionChallenges"."publicId" = @Id
                """,
                new { Id = request.ChallengeId },
                tx,
                cancellationToken: ct));
            await tx.CommitAsync(ct);
            throw new PortalValidationException("Check your six-digit code and try again.");
        }

        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/AccountManagementRepository/CountAccountDeletionRequestsForUser"),
            new { User = user },
            tx,
            cancellationToken: ct)) > 0)
        {
            throw new PortalConflictException("A deletion request is already in progress.");
        }

        if (request.Reason?.Length > 500)
        {
            throw new PortalValidationException("Keep the reason within 500 characters.");
        }

        var id = Guid.NewGuid();
        var reference = "AR-" + DateTimeOffset.UtcNow.Year + "-" + RandomNumberGenerator.GetInt32(1000000, 10000000);
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/AccountManagementRepository/MarkAccountDeletionChallengesUsedForUser"),
            new
            {
                Challenge = request.ChallengeId,
                Id = id,
                User = user,
                Reference = reference,
                Reason = secretProtector.Protect(request.Reason?.Trim() ?? ""),
                Now = DateTimeOffset.UtcNow
            },
            tx,
            cancellationToken: ct));
        await AuditOperation(c, tx, user, "AccountDeletionRequested", id, new { }, ct);
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<IReadOnlyList<AccountDeletionDto>> GetDeletionRequests(Guid actor, bool administration, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        if (administration)
        {
            await RequireSupervisor(c, null, actor, ct, true);
        }

        var rows = (await c.QueryAsync<AccountDeletionDto>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/AccountManagementRepository/GetAccountDeletionRequestsForActor",
            ("TrueValue", dialect.TrueLiteral)),
            new { Administration = administration, Actor = actor },
            cancellationToken: ct))).AsList();
        foreach (var row in rows)
        {
            row.Reason = secretProtector.Unprotect(row.Reason);
            if (row.Decision is not null)
            {
                row.Decision = secretProtector.Unprotect(row.Decision);
            }
        }

        return rows;
    }

    public async Task CancelDeletionRequest(Guid user, Guid request, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        var count = await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/AccountManagementRepository/MarkAccountDeletionRequestsCancelledForUser"),
            new { Id = request, User = user },
            tx,
            cancellationToken: ct));
        if (count != 1)
        {
            throw new PortalConflictException("This request can no longer be cancelled.");
        }

        await AuditOperation(c, tx, user, "AccountDeletionCancelled", request, new { }, ct);
        await tx.CommitAsync(ct);
    }

    public async Task ReviewDeletionRequest(Guid actor, Guid request, ReviewDeletionRequest decision, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(decision.Comment) || decision.Comment.Trim().Length is < 10 or > 500)
        {
            throw new PortalValidationException("Add a review note between 10 and 500 characters.");
        }

        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        await RequireSupervisor(c, tx, actor, ct, true);
        var row = await c.QuerySingleOrDefaultAsync<AccountDeletionDto>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/AccountManagementRepository/GetAccountDeletionRequest"),
            new { Id = request },
            tx,
            cancellationToken: ct)) ?? throw new PortalNotFoundException();
        if (row.UserId == actor)
        {
            throw new PortalForbiddenException();
        }

        if (row.Version != decision.ExpectedVersion)
        {
            throw new PortalConflictException("This request changed. Refresh before reviewing it.");
        }

        if (!(row.Status == "Pending"
            && decision.Status is "UnderReview" or "Declined")
            && !(row.Status == "UnderReview"
            && decision.Status is "Completed" or "Declined"))
        {
            throw new PortalConflictException("Choose a valid next review step.");
        }

        if (decision.Status == "Completed")
        {
            var targetRoles = await Roles(c, tx, row.UserId, ct);
            if (!targetRoles.Contains("customer") || targetRoles.Any(role => role != "customer"))
            {
                throw new PortalConflictException("Only active customer accounts can be completed through this workflow.");
            }

            if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/AccountManagementRepository/CountDisputesForUser"),
                new { User = row.UserId },
                tx,
                cancellationToken: ct)) > 0)
            {
                throw new PortalConflictException("Resolve the customer's open disputes before completing deletion.");
            }

            await c.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/AccountManagementRepository/SoftDeleteUsersForUserAndActor"),
                new
                {
                    User = row.UserId,
                    Actor = actor,
                    Closed = "closed-" + row.UserId.ToString("N"),
                    Normalized = ("closed-" + row.UserId.ToString("N")).ToUpperInvariant(),
                    Now = DateTimeOffset.UtcNow,
                    False = false
                },
                tx,
                cancellationToken: ct));
        }

        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/AccountManagementRepository/UpdateAccountDeletionRequestsForActor"),
            new
            {
                decision.Status,
                Decision = secretProtector.Protect(decision.Comment.Trim()),
                Now = DateTimeOffset.UtcNow,
                Actor = actor,
                Id = request
            },
            tx,
            cancellationToken: ct));
        await AuditOperation(c, tx, actor, "AccountDeletion" + decision.Status, request, new { CustomerId = row.UserId }, ct);
        await tx.CommitAsync(ct);
    }

    private class DeletionCode
    {
        public string CodeHash { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public int Attempts { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }

        public DeletionCode()
        {
        }

        public DeletionCode(string CodeHash, string State, int Attempts, DateTimeOffset ExpiresAt)
        {
            this.CodeHash = CodeHash;
            this.State = State;
            this.Attempts = Attempts;
            this.ExpiresAt = ExpiresAt;
        }
    }
}
