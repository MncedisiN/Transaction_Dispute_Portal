using DisputePortal.Api.Configuration;
using Dapper;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Api.Models.Domain;
using System.Data.Common;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Security;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;

public class DisputeRiskRepository(
    IDatabaseConnectionFactory factory,
    IDatabaseDialect databaseDialect,
    TimeZoneInfo businessTimeZone,
    IOtpHasher otpHasher,
    ApiRuntimeOptions runtime) : RepositoryBase(factory, databaseDialect), IDisputeRiskRepository
{
    private static DisputeRiskPolicy Domain(RiskPolicyVersionDto policy) => new global::DisputePortal.Api.Models.Domain.DisputeRiskPolicy
    {
        Version = policy.Version,
        HighValueThreshold = policy.HighValueThreshold,
        ExtraCheckAfterDays = policy.ExtraCheckAfterDays,
        DebitOrderWindowDays = policy.DebitOrderWindowDays,
        DebitOrderAppLimit = policy.DebitOrderAppLimit
    };

    public async Task<DisputeRiskPolicy> GetRiskPolicy(CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        return Domain(await PolicyVersionRepository.EffectiveRouting(c, null, DateTimeOffset.UtcNow, ct));
    }

    public async Task<PolicyDefaultsDto> GetPolicyDefaults(CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        return await ReadPolicyDefaults(c, null, ct);
    }

    private static async Task<PolicyDefaultsDto> ReadPolicyDefaults(DbConnection c, DbTransaction? tx, CancellationToken ct)
    {
        using var results = await c.QueryMultipleAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRiskRepository/GetInitialDisputePolicies"),
            transaction: tx,
            cancellationToken: ct));
        return new(
            await results.ReadSingleAsync<DisputeRiskPolicy>(),
            await results.ReadSingleAsync<AutomaticRefundPolicyDto>(),
            new(DisputePolicyLimits.MaximumMoney, DisputePolicyLimits.MaximumDays, DisputePolicyLimits.MaximumHours));
    }

    public async Task<PolicyConfigurationDto> ResetPolicies(Guid actor, ResetDisputePoliciesRequest request, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        await RequireSupervisor(c, tx, actor, ct, true);
        if (request.RequestId == Guid.Empty)
        {
            throw new PortalValidationException("Supply a stable reset request ID.");
        }

        var previouslyPublished = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRiskRepository/CountPolicyPublicationsByRequestId"),
            new { Request = request.RequestId },
            tx,
            cancellationToken: ct));
        var baseline = await ReadPolicyDefaults(c, tx, ct);
        var store = new PolicyVersionRepository(connections, dialect);
        var r = baseline.Submission;
        var a = baseline.Automatic;
        var riskRequest = new RiskPolicyUpdateRequest(
            request.ExpectedRiskVersion,
            r.HighValueThreshold,
            r.ExtraCheckAfterDays,
            r.DebitOrderWindowDays,
            r.DebitOrderAppLimit,
            request.Reason)
        {
            RequestId = request.RequestId,
            ConcurrencyToken = request.RiskConcurrencyToken ?? request.ExpectedRiskVersion
        };
        var automaticRequest = new UpdateAutomaticRefundPolicyRequest(
            request.ExpectedAutomaticVersion,
            a.Enabled,
            a.MaximumAmount,
            a.MaximumAgeHours,
            a.DailyLimit,
            request.Reason)
        {
            RequestId = request.RequestId,
            ConcurrencyToken = request.AutomaticConcurrencyToken ?? request.ExpectedAutomaticVersion
        };
        var risk = await store.ChangeLocked<RiskPolicyVersionDto>(c, tx, actor, false, riskRequest, true, ct);
        var automatic = await store.ChangeLocked<AutomaticRefundPolicyDto>(c, tx, actor, true, automaticRequest, true, ct);
        if (previouslyPublished != 2)
        {
            await AuditOperation(
                c,
                tx,
                actor,
                "DisputePolicyDefaultsRestored",
                Guid.Empty,
                new
                {
                    request.RequestId,
                    Risk = risk.Proposed.Version,
                    Automatic = automatic.Proposed.Version,
                    request.Reason
                },
                ct);
        }

        await tx.CommitAsync(ct);
        return new(Domain(risk.Proposed), automatic.Proposed);
    }

    public Task UpdateRiskPolicy(Guid actor, RiskPolicyUpdateRequest request, CancellationToken ct) => new PolicyVersionRepository(
        connections,
        dialect).PublishRouting(actor, request, ct);
    // Reads actual intake state on the caller's transaction during creation and OTP consumption.
    private async Task<DisputeIntakeDto> AssessIntake(DbConnection c, DbTransaction? tx, Guid user, Guid transaction, CancellationToken ct)
    {
        var item = await c.QuerySingleOrDefaultAsync<RiskTransaction>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/DisputeRiskRepository/GetTransactionDisputeEligibilityFacts",
            ("EligibleTransactionExpression", dialect.EligibleTransactionExpression),
            ("ExistsAsBoolean", dialect.ExistsAsBoolean(SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRiskRepository/CheckPendingOrPostedFinancialCommandForTransaction"))),
            ("RelatedExistsAsBoolean", dialect.ExistsAsBoolean(SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRiskRepository/CheckUnrecalledRefundForTransaction")))),
            new { Transaction = transaction, User = user },
            tx,
            cancellationToken: ct)) ?? throw new PortalNotFoundException();
        var evaluationTime = DateTimeOffset.UtcNow;
        var snapshot = await PolicyVersionRepository.EffectiveRouting(c, tx, evaluationTime, ct);
        var decision = snapshot.Enabled ? Domain(snapshot).AssessTransactionDisputeRisk(
            item.Amount,
            item.Currency,
            item.TransactionType,
            item.TransactionAt,
            evaluationTime,
            businessTimeZone,
            item.Scheme,
            item.Mandate) : new DisputeRiskDecision
            {
                Allowed = false,
                RequiresVerification = false,
                AssistedReview = false,
                AgeDays = 0,
                PolicyVersion = snapshot.Version,
                Message = "New disputes are temporarily disabled by the published policy. Contact support."
            };
        var result = new DisputeIntakeDto(decision)
        {
            PolicyVersionPublicId = snapshot.VersionPublicId,
            ActiveDisputeId = item.ActiveDisputeId,
            HasPendingFinancialOperation = item.HasPendingFinancialOperation,
            HasPostedRefund = item.HasPostedRefund
        };
        string? reason = null;
        if (item.ActiveDisputeId.HasValue)
        {
            reason = "A dispute is already active for this transaction. View the existing dispute.";
        }
        else if (item.HasPendingFinancialOperation)
        {
            reason = "A financial operation for this transaction is still processing. Wait for its confirmed result.";
        }
        else if (item.HasPostedRefund)
        {
            reason = "This transaction has already been refunded. View its dispute history or contact support.";
        }
        else if (!item.Eligible)
        {
            reason = "This transaction is not eligible for a new dispute.";
        }

        if (reason is not null)
        {
            result.Allowed = false;
            result.RequiresVerification = false;
            result.AssistedReview = false;
            result.CanStart = false;
            result.BlockReason = reason;
            result.Message = reason;
        }

        return result;
    }

    internal async Task<DisputeRiskDecision> AssessLocked(
        DbConnection c,
        DbTransaction? tx,
        Guid user,
        Guid transaction,
        CancellationToken ct)
    {
        var decision = await AssessIntake(c, tx, user, transaction, ct);
        return new DisputeRiskDecision
        {
            Allowed = decision.Allowed,
            RequiresVerification = decision.RequiresVerification,
            AssistedReview = decision.AssistedReview,
            AgeDays = decision.AgeDays,
            PolicyVersion = decision.PolicyVersion,
            Message = decision.Message,
            PolicyVersionPublicId = decision.PolicyVersionPublicId
        };
    }

    public async Task<DisputeIntakeDto> AssessDispute(Guid user, Guid transaction, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        return await AssessIntake(c, null, user, transaction, ct);
    }

    public async Task<IdentityChallengeDto> ReserveDisputeVerification(
        Guid user,
        Guid session,
        Guid transaction,
        string channel,
        string destination,
        string code,
        CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        if (channel is not ("email" or "sms" or "whatsapp"))
        {
            throw new PortalValidationException("Choose email, SMS or WhatsApp verification.");
        }

        var decision = await AssessLocked(c, tx, user, transaction, ct);
        if (!decision.Allowed)
        {
            throw new PortalConflictException(decision.Message);
        }

        var now = DateTimeOffset.UtcNow;
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRiskRepository/CheckActiveDisputeVerificationSession"),
            new
            {
                Session = session,
                User = user,
                Now = now
            },
            tx,
            cancellationToken: ct)) != 1)
        {
            throw new PortalForbiddenException();
        }

        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRiskRepository/CountRecentDisputeVerificationChallengesForUser"),
            new { User = user, Since = now.AddSeconds(-runtime.VerificationResendDelaySeconds) },
            tx,
            cancellationToken: ct)) > 0)
        {
            throw new PortalConflictException($"Wait {runtime.VerificationResendDelaySeconds} seconds before requesting another code.");
        }

        var id = Guid.NewGuid();
        var expires = now.AddMinutes(runtime.VerificationCodeLifetimeMinutes);
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRiskRepository/ExpireActiveDisputeVerificationChallenges"),
            new { User = user, Transaction = transaction },
            tx,
            cancellationToken: ct));
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRiskRepository/InsertDisputeVerificationChallenge"),
            new
            {
                Id = id,
                User = user,
                Session = session,
                Transaction = transaction,
                Version = decision.PolicyVersion,
                PolicySnapshot = decision.PolicyVersionPublicId,
                Channel = channel,
                Hash = otpHasher.Hash(id, code),
                Now = now,
                Expires = expires
            },
            tx,
            cancellationToken: ct));
        await tx.CommitAsync(ct);
        return new IdentityChallengeDto(id, channel, MaskDestination(destination), expires);
    }

    private static string MaskDestination(string value) =>
        value.Contains('@') ? value[..1] + "•••" + value[value.IndexOf('@')..] : "••••" + value[^Math.Min(4, value.Length)..];

    public async Task ReadyDisputeVerification(Guid user, Guid challenge, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRiskRepository/MarkDisputeVerificationChallengeReady"),
            new { Id = challenge, User = user },
            cancellationToken: ct));
    }

    public async Task CompleteDisputeVerification(
        Guid user,
        Guid session,
        Guid transaction,
        Guid challenge,
        string code,
        CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        var row = await c.QuerySingleOrDefaultAsync<RiskChallenge>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRiskRepository/GetOwnedDisputeVerificationChallenge"),
            new
            {
                Id = challenge,
                User = user,
                Session = session,
                Transaction = transaction
            },
            tx,
            cancellationToken: ct));
        if (row is null
            || row.State != "Ready"
            || row.Channel is not ("email" or "sms" or "whatsapp")
            || row.Attempts >= 5
            || row.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new PortalValidationException("Request a fresh verification code.");
        }

        var valid = otpHasher.Matches(row.Id, code, row.CodeHash);
        await c.ExecuteAsync(new CommandDefinition(
            """
            UPDATE "identity"."disputeVerifications"
            SET
                "attempts" = "disputeVerifications"."attempts" + 1,
                "state" = @State
            WHERE "disputeVerifications"."publicId" = @Id
            """,
            new { Id = challenge, State = valid ? "Verified" : row.Attempts >= 4 ? "Failed" : "Ready" },
            tx,
            cancellationToken: ct));
        await AuditOperation(
            c,
            tx,
            user,
            valid ? "DisputeVerificationPassed" : "DisputeVerificationRejected",
            transaction,
            new { ChallengeId = challenge },
            ct);
        await tx.CommitAsync(ct);
        if (!valid)
        {
            throw new PortalValidationException("Check the code and try again.");
        }
    }

    // Consuming verification and creating the dispute must commit or roll back together.
    internal async Task<DisputeRiskDecision> EnforceRisk(
        DbConnection c,
        DbTransaction tx,
        Guid user,
        Guid transaction,
        Guid? session,
        Guid? challenge,
        CancellationToken ct)
    {
        var decision = await AssessLocked(c, tx, user, transaction, ct);
        if (!decision.Allowed)
        {
            throw new PortalConflictException(decision.Message);
        }

        if (decision.RequiresVerification)
        {
            var count = await c.ExecuteAsync(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/DisputeRiskRepository/ConsumeVerifiedDisputeChallenge"),
                new
                {
                    Id = challenge,
                    User = user,
                    Session = session,
                    Transaction = transaction,
                    Version = decision.PolicyVersion,
                    PolicySnapshot = decision.PolicyVersionPublicId,
                    Now = DateTimeOffset.UtcNow
                },
                tx,
                cancellationToken: ct));
            if (count != 1)
            {
                throw new PortalConflictException("Complete the extra security check for this transaction before submitting. Rules may have changed.");
            }
        }

        return decision;
    }

    private class RiskTransaction
    {
        public bool Eligible { get; set; }
        public Guid? ActiveDisputeId { get; set; }
        public bool HasPendingFinancialOperation { get; set; }
        public bool HasPostedRefund { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "";
        public string TransactionType { get; set; } = "";
        public DateTimeOffset TransactionAt { get; set; }
        public string? Scheme { get; set; }
        public string? Mandate { get; set; }
    }

    private class RiskChallenge
    {
        public Guid Id { get; set; }
        public int PolicyVersion { get; set; }
        public string Channel { get; set; } = "";
        public string CodeHash { get; set; } = "";
        public string State { get; set; } = "";
        public int Attempts { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
