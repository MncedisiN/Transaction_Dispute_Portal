using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;
using DisputePortal.Platform.Contracts;
using DisputePortal.Api.Models.Persistence;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;

public class PolicyVersionRepository(IDatabaseConnectionFactory factory, IDatabaseDialect databaseDialect) : RepositoryBase(
    factory,
    databaseDialect)
{
    // The two table names and column lists are application-owned SQL, never input.
    private static string Table(bool automatic) => automatic ? "refund" : "risk";

    private static string Fields(bool automatic) =>
        automatic ? "v.enabled AS Enabled,v.\"maximumAmount\" AS MaximumAmount,v.\"maximumAgeHours\" AS MaximumAgeHours,v.\"dailyLimit\" AS DailyLimit,v.\"duplicateWindowMinutes\" AS DuplicateWindowMinutes" : "v.enabled AS Enabled,v.\"highValueThreshold\" AS HighValueThreshold,v.\"extraCheckAfterDays\" AS ExtraCheckAfterDays,v.\"debitOrderWindowDays\" AS DebitOrderWindowDays,v.\"debitOrderAppLimit\" AS DebitOrderAppLimit";

    private static string Select(bool automatic) => SqlScripts.RenderSqlScript(
        "Shared/Queries/Api/PolicyVersionRepository/SelectPolicyVersionDetailsWithAuditActors",
        ("Fields", Fields(automatic)),
        ("Table", Table(automatic)),
        ("RelatedTable", Table(automatic)));

    private static DateTimeOffset Precision(DateTimeOffset value) => new(value.UtcTicks / 10 * 10, TimeSpan.Zero);

    private static DateTimeOffset Now() => Precision(DateTimeOffset.UtcNow);

    private static Task<PolicyIdentityRow> Identity(DbConnection c, DbTransaction? tx, bool automatic, CancellationToken ct) =>
        c.QuerySingleAsync<PolicyIdentityRow>(new CommandDefinition(
        SqlScripts.RenderSqlScript(
        "Shared/Queries/Api/PolicyVersionRepository/GetDefaultPolicyIdentityAndConcurrency",
        ("Table", Table(automatic))),
        transaction: tx,
        cancellationToken: ct));

    private static async Task<List<T>> Candidates<T>(
        DbConnection c,
        DbTransaction? tx,
        bool automatic,
        DateTimeOffset at,
        CancellationToken ct)
        where T : PolicyVersionMetadata => (await c.QueryAsync<T>(new CommandDefinition(
        SqlScripts.RenderSqlScript(
        "Shared/Queries/Api/PolicyVersionRepository/GetPolicyVersionByAt",
        ("SelectQuery", Select(automatic)),
        ("Table", Table(automatic))),
        new { At = at },
        tx,
        cancellationToken: ct))).ToList();

    public static async Task<AutomaticRefundPolicyDto?> EffectiveAutomatic(
        DbConnection c,
        DbTransaction? tx,
        DateTimeOffset at,
        CancellationToken ct) => PolicyVersionResolver.ResolvePublishedPolicyVersion(await Candidates<AutomaticRefundPolicyDto>(
        c,
        tx,
        true,
        at,
        ct), "Global", at).Selected;

    public static async Task<RiskPolicyVersionDto> EffectiveRouting(
        DbConnection c,
        DbTransaction? tx,
        DateTimeOffset at,
        CancellationToken ct) => PolicyVersionResolver.ResolvePublishedPolicyVersion(await Candidates<RiskPolicyVersionDto>(
        c,
        tx,
        false,
        at,
        ct), "Global", at).Selected ?? throw new PortalConflictException("No dispute-routing policy applies at this time. Contact support.");

    public Task<PolicyHistoryDto<AutomaticRefundPolicyDto>> AutomaticHistory(
        Guid actor,
        int page,
        int pageSize,
        string? search,
        CancellationToken ct) => History<AutomaticRefundPolicyDto>(actor, true, page, pageSize, search, ct);

    public Task<PolicyHistoryDto<RiskPolicyVersionDto>> RoutingHistory(
        Guid actor,
        int page,
        int pageSize,
        string? search,
        CancellationToken ct) => History<RiskPolicyVersionDto>(actor, false, page, pageSize, search, ct);

    private async Task<PolicyHistoryDto<T>> History<T>(
        Guid actor,
        bool automatic,
        int page,
        int pageSize,
        string? search,
        CancellationToken ct)
        where T : PolicyVersionMetadata
    {
        if (page < 1 || pageSize is < 1 or > 100)
        {
            throw new PortalValidationException("Use a valid page and a page size from 1 to 100.");
        }

        await using var c = await connections.OpenConnection(ct);
        await RequireSupervisor(c, null, actor, ct, true);
        var at = Now();
        var identity = await Identity(c, null, automatic, ct);
        var candidates = await Candidates<T>(c, null, automatic, at, ct);
        var searchValue = CleanSearch(search)?.ToLowerInvariant().Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace(
            "[",
            "~[");
        var where = SqlScripts.RenderSqlScript("Shared/Queries/Api/PolicyVersionRepository/FilterPolicyVersionByVisibility", ("SearchPredicate", dialect.LikeExpression(
            "v.\"changeReason\"",
            "@Search")), ("RelatedSearchPredicate", dialect.LikeExpression(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/PolicyVersionRepository/FilterPolicyVersionRecords"),
                "@Search")));
        var rows = (await c.QueryAsync<T>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/PolicyVersionRepository/GetPolicyVersionPage",
            ("SelectQuery", Select(automatic)),
            ("FilterClause", where),
            ("PaginationClause", dialect.PaginationClause)),
            new
            {
                Search = searchValue,
                Offset = PageOffset(page, pageSize),
                PageSize = pageSize
            },
            cancellationToken: ct))).ToArray();
        var total = await c.ExecuteScalarAsync<long>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/PolicyVersionRepository/CountFilteredPolicyVersionHistory",
            ("Table", Table(automatic)),
            ("RelatedTable", Table(automatic)),
            ("FilterClause", where)),
            new { Search = searchValue },
            cancellationToken: ct));
        var resolution = PolicyVersionResolver.ResolvePublishedPolicyVersion(candidates, "Global", at);
        PolicyVersionResolver.ResolvePublishedPolicyVersion(candidates.Concat(rows), "Global", at);
        return new()
        {
            Items = rows,
            Total = total,
            Page = page,
            PageSize = pageSize,
            ConcurrencyToken = identity.ConcurrencyToken,
            Effective = resolution
        };
    }

    public Task<PolicyPreviewDto<AutomaticRefundPolicyDto>> PreviewAutomatic(
        Guid actor,
        UpdateAutomaticRefundPolicyRequest request,
        CancellationToken ct) => Change<AutomaticRefundPolicyDto>(actor, true, request, false, ct);

    public Task<PolicyPreviewDto<RiskPolicyVersionDto>> PreviewRouting(Guid actor, RiskPolicyUpdateRequest request, CancellationToken ct) =>
        Change<RiskPolicyVersionDto>(actor, false, request, false, ct);

    public async Task PublishAutomatic(Guid actor, UpdateAutomaticRefundPolicyRequest request, CancellationToken ct) =>
        _ = await Change<AutomaticRefundPolicyDto>(actor, true, request, true, ct);

    public async Task PublishRouting(Guid actor, RiskPolicyUpdateRequest request, CancellationToken ct) =>
        _ = await Change<RiskPolicyVersionDto>(actor, false, request, true, ct);

    private async Task<PolicyPreviewDto<T>> Change<T>(
        Guid actor,
        bool automatic,
        PolicyPublicationRequest request,
        bool publish,
        CancellationToken ct)
        where T : PolicyVersionMetadata, new()
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        await RequireSupervisor(c, tx, actor, ct, true);
        var result = await ChangeLocked<T>(c, tx, actor, automatic, request, publish, ct);
        await tx.CommitAsync(ct);
        return result;
    }

    internal async Task<PolicyPreviewDto<T>> ChangeLocked<T>(
        DbConnection c,
        DbTransaction tx,
        Guid actor,
        bool automatic,
        PolicyPublicationRequest request,
        bool publish,
        CancellationToken ct)
        where T : PolicyVersionMetadata, new()
    {
        var at = Now();
        var identity = await Identity(c, tx, automatic, ct);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, request.GetType()))));
        if (request.RequestId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length is < 10 or > 500
            || request.Priority is < -1000 or > 1000)
        {
            throw new PortalValidationException("Use a request ID, priority from -1000 to 1000 and a reason of 10-500 characters.");
        }

        var existing = await c.QuerySingleOrDefaultAsync<T>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/PolicyVersionRepository/GetPolicyVersionByPolicyAndRequest",
            ("SelectQuery", Select(automatic))),
            new { Policy = identity.Id, Request = request.RequestId },
            tx,
            cancellationToken: ct));
        var candidates = await Candidates<T>(c, tx, automatic, at, ct);
        var before = PolicyVersionResolver.ResolvePublishedPolicyVersion(candidates, "Global", at);
        if (before.Selected is not null)
        {
            before.Selected = JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(before.Selected))!;
        }

        if (existing is not null)
        {
            var stored = await c.QuerySingleAsync<string>(new CommandDefinition(
                SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/PolicyVersionRepository/GetPolicyRequestFingerprint",
                ("Table", Table(automatic))),
                new { Request = request.RequestId, Policy = identity.Id },
                tx,
                cancellationToken: ct));
            if (stored != fingerprint || existing.CreatedBy != actor)
            {
                throw new PortalConflictException("This request ID was already used for different policy content.");
            }

            return new()
            {
                Before = before,
                After = before,
                Proposed = existing
            };
        }

        if (request.ConcurrencyToken != identity.ConcurrencyToken)
        {
            throw new PortalConflictException("Policy history changed. Your input is preserved; reload the summary and review it before publishing.");
        }

        var proposed = new T();
        if (request.Operation is "Copy" or "Restore")
        {
            if (request.SourceVersionPublicId is null)
            {
                throw new PortalValidationException("Choose a source version.");
            }

            proposed = await c.QuerySingleOrDefaultAsync<T>(new CommandDefinition(
                SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/PolicyVersionRepository/GetPolicyVersionByPolicyAndSource",
                ("SelectQuery", Select(automatic))),
                new { Policy = identity.Id, Source = request.SourceVersionPublicId },
                tx,
                cancellationToken: ct)) ?? throw new PortalNotFoundException();
        }
        else if (request.Operation != "Create" || request.SourceVersionPublicId is not null)
        {
            throw new PortalValidationException("Use Create, Copy or Restore with the matching source selection.");
        }

        if (request.Operation == "Create")
        {
            if (automatic && request is UpdateAutomaticRefundPolicyRequest a && proposed is AutomaticRefundPolicyDto target)
            {
                target.Enabled = a.Enabled;
                target.MaximumAmount = a.MaximumAmount;
                target.MaximumAgeHours = a.MaximumAgeHours;
                target.DailyLimit = a.DailyLimit;
                target.DuplicateWindowMinutes = a.DuplicateWindowMinutes
                    ?? (before.Selected as AutomaticRefundPolicyDto)?.DuplicateWindowMinutes
                    ?? await c.QuerySingleAsync<int>(new CommandDefinition(
                        "SELECT \"duplicateWindowMinutes\" FROM configuration.\"refundPolicies\" WHERE \"version\" = 0",
                        transaction: tx,
                        cancellationToken: ct));
            }
            else if (!automatic && request is RiskPolicyUpdateRequest r && proposed is RiskPolicyVersionDto routing)
            {
                routing.Enabled = r.Enabled;
                routing.HighValueThreshold = r.HighValueThreshold;
                routing.ExtraCheckAfterDays = r.ExtraCheckAfterDays;
                routing.DebitOrderWindowDays = r.DebitOrderWindowDays;
                routing.DebitOrderAppLimit = r.DebitOrderAppLimit;
            }
            else
            {
                throw new PortalValidationException("Policy content does not match its type.");
            }
        }

        ValidatePolicyChangeRequest(proposed);
        var from = request.EffectiveFromUtc is { } start ? Precision(start) : at;
        DateTimeOffset? until = request.EffectiveToUtc is { } end ? Precision(end) : null;
        if (from < at || until <= from)
        {
            throw new PortalValidationException("Use a current or future start and an expiry later than the start.");
        }

        var last = await c.QuerySingleAsync<DateTimeOffset>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/PolicyVersionRepository/GetPolicyVersionEffectiveStart",
            ("Table", Table(automatic))),
            new { Policy = identity.Id, Version = identity.LastVersion },
            tx,
            cancellationToken: ct));
        if (from <= last)
        {
            throw new PortalConflictException("A later version is already scheduled. Publish after its start; earlier versions cannot override it.");
        }

        var conflict = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/PolicyVersionRepository/CountConflictingPublishedPolicyVersions",
            ("Table", Table(automatic)),
            ("RelatedTable", Table(automatic)),
            ("AdditionalTable", Table(automatic))),
            new
            {
                Policy = identity.Id,
                identity.Scope,
                request.Priority,
                Deleted = false,
                From = from,
                Until = until
            },
            tx,
            cancellationToken: ct));
        if (conflict != 0)
        {
            throw new PortalConflictException("Another policy in this scope has an overlapping schedule at the same priority.");
        }

        proposed.PolicyPublicId = identity.PublicId;
        proposed.VersionPublicId = Guid.NewGuid();
        proposed.Version = checked(identity.LastVersion + 1);
        proposed.ConcurrencyToken = checked(identity.ConcurrencyToken + 1);
        proposed.Scope = identity.Scope;
        proposed.Priority = request.Priority;
        proposed.EffectiveFromUtc = from;
        proposed.EffectiveToUtc = until;
        proposed.PublishedAtUtc = at;
        proposed.CreatedAtUtc = at;
        proposed.CreatedBy = actor;
        proposed.CreatedByName = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/PolicyVersionRepository/GetCustomerProfileForActor"),
            new { Actor = actor },
            tx,
            cancellationToken: ct));
        proposed.DeletedByName = null;
        proposed.ChangeReason = request.Reason.Trim();
        proposed.IsDeleted = false;
        proposed.DeletedAtUtc = null;
        proposed.DeletedBy = null;
        proposed.DeletionReason = null;
        proposed.Provenance = "Published";
        proposed.RequestId = request.RequestId;
        proposed.SourceVersionPublicId = request.SourceVersionPublicId;
        var after = PolicyVersionResolver.ResolvePublishedPolicyVersion(candidates.Append(proposed), "Global", at);
        if (publish)
        {
            var args = new DynamicParameters(proposed);
            args.Add("Actor", actor);
            args.Add("Policy", identity.Id);
            args.Add("LastVersion", identity.LastVersion);
            args.Add("Fingerprint", fingerprint);
            var columns = automatic ? "enabled,\"maximumAmount\",\"maximumAgeHours\",\"dailyLimit\",\"duplicateWindowMinutes\"" : "enabled,\"highValueThreshold\",\"extraCheckAfterDays\",\"debitOrderWindowDays\",\"debitOrderAppLimit\"";
            var values = automatic ? "@Enabled,@MaximumAmount,@MaximumAgeHours,@DailyLimit,@DuplicateWindowMinutes" : "@Enabled,@HighValueThreshold,@ExtraCheckAfterDays,@DebitOrderWindowDays,@DebitOrderAppLimit";
            await c.ExecuteAsync(new CommandDefinition(
                SqlScripts.RenderSqlScript(
                "Shared/Queries/Api/PolicyVersionRepository/InsertPolicyVersionAndAdvancePolicy",
                ("Table", Table(automatic)),
                ("SelectedColumns", columns),
                ("RelatedTable", Table(automatic)),
                ("Values", values),
                ("AdditionalTable", Table(automatic))),
                args,
                tx,
                cancellationToken: ct));
            await AuditOperation(
                c,
                tx,
                actor,
                "PolicyVersionPublished",
                proposed.VersionPublicId,
                new
                {
                    proposed.PolicyPublicId,
                    proposed.Version,
                    proposed.Priority,
                    proposed.EffectiveFromUtc,
                    proposed.EffectiveToUtc,
                    request.Operation,
                    request.SourceVersionPublicId,
                    Reason = request.Reason.Trim()
                },
                ct);
            await AuditOperation(
                c,
                tx,
                actor,
                "PolicyVersionSupersessionScheduled",
                proposed.VersionPublicId,
                new
                {
                    PreviousVersion = identity.LastVersion,
                    From = from,
                    Reason = request.Reason.Trim()
                },
                ct);
            if (request.Operation != "Create")
            {
                await AuditOperation(
                    c,
                    tx,
                    actor,
                    "PolicyVersion" + request.Operation,
                    proposed.VersionPublicId,
                    new
                    {
                        request.SourceVersionPublicId,
                        proposed.Version,
                        Reason = request.Reason.Trim()
                    },
                    ct);
            }
        }

        var activationRows = JsonSerializer.Deserialize<T[]>(JsonSerializer.Serialize(candidates.Append(proposed)))!;
        return new()
        {
            Before = before,
            After = after,
            AtActivation = PolicyVersionResolver.ResolvePublishedPolicyVersion(activationRows, "Global", from),
            Proposed = proposed
        };
    }

    private static void ValidatePolicyChangeRequest(PolicyVersionMetadata value)
    {
        if (value is AutomaticRefundPolicyDto a
            && !new AutomaticRefundPolicy
            {
                Version = 1,
                Enabled = a.Enabled,
                MaximumAmount = a.MaximumAmount,
                MaximumAgeHours = a.MaximumAgeHours,
                DuplicateWindowMinutes = a.DuplicateWindowMinutes,
                DailyLimit = a.DailyLimit
            }.IsValid())
        {
            throw new PortalValidationException("Choose valid automatic refund limits.");
        }

        if (value is RiskPolicyVersionDto r)
        {
            try
            {
                new DisputeRiskPolicy
                {
                    Version = 1,
                    HighValueThreshold = r.HighValueThreshold,
                    ExtraCheckAfterDays = r.ExtraCheckAfterDays,
                    DebitOrderWindowDays = r.DebitOrderWindowDays,
                    DebitOrderAppLimit = r.DebitOrderAppLimit
                }.ValidateDisputeRiskPolicy();
            }
            catch (ArgumentException e)
            {
                throw new PortalValidationException(e.Message);
            }
        }
    }

    public async Task<PolicyResolution<PolicyVersionMetadata>> SoftDeletePolicyVersion(
        Guid actor,
        bool automatic,
        Guid version,
        DeletePolicyVersionRequest request,
        bool preview,
        CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        await RequireSupervisor(c, tx, actor, ct, true);
        var identity = await Identity(c, tx, automatic, ct);
        var at = Now();
        var row = await c.QuerySingleOrDefaultAsync<PolicyVersionMetadata>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/PolicyVersionRepository/GetPolicyVersionByPolicyAndVersion",
            ("SelectQuery", Select(automatic))),
            new { Policy = identity.Id, Version = version },
            tx,
            cancellationToken: ct)) ?? throw new PortalNotFoundException();
        if (!row.IsDeleted)
        {
            if (request.ConcurrencyToken != identity.ConcurrencyToken)
            {
                throw new PortalConflictException("Policy history changed. Review the current deletion preview.");
            }

            if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 10 or > 500)
            {
                throw new PortalValidationException("Explain the deletion in 10-500 characters.");
            }

            if (!preview)
            {
                await c.ExecuteAsync(new CommandDefinition(
                    SqlScripts.RenderSqlScript(
                    "Shared/Queries/Api/PolicyVersionRepository/SoftDeletePolicyVersionAndAdvanceConcurrency",
                    ("Table", Table(automatic)),
                    ("RelatedTable", Table(automatic))),
                    new
                    {
                        Deleted = true,
                        At = at,
                        Actor = actor,
                        Reason = request.Reason.Trim(),
                        Version = version,
                        Policy = identity.Id
                    },
                    tx,
                    cancellationToken: ct));
                await AuditOperation(
                    c,
                    tx,
                    actor,
                    "PolicyVersionDeleted",
                    version,
                    new
                    {
                        row.PolicyPublicId,
                        row.Version,
                        Reason = request.Reason.Trim()
                    },
                    ct);
            }
        }

        var rows = await Candidates<PolicyVersionMetadata>(c, tx, automatic, at, ct);
        foreach (var item in rows.Where(x => x.VersionPublicId == version))
            item.IsDeleted = true;
        var result = PolicyVersionResolver.ResolvePublishedPolicyVersion(rows, "Global", at);
        if (row.EffectiveFromUtc > at)
        {
            result.AtAffectedBoundary = PolicyVersionResolver.ResolvePublishedPolicyVersion(
                JsonSerializer.Deserialize<PolicyVersionMetadata[]>(JsonSerializer.Serialize(rows))!,
                "Global",
                row.EffectiveFromUtc);
        }

        await tx.CommitAsync(ct);
        return result;
    }
}
