using System.Text.Json;
using DisputePortal.Api.Models.Exceptions;
using System.Text.Json.Nodes;
using CountryReferenceCatalog = DisputePortal.Api.Platform.CountryCatalog;
using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Platform;
using DisputePortal.Api.Service.Banking;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Banking;
using DisputePortal.Platform.Infrastructure.Models;
using DisputePortal.Platform.Infrastructure.Interfaces;

namespace DisputePortal.Api.Controllers;

[ApiController]
[Route("api/v1/platform")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PlatformController(
    IServiceProvider services,
    IConfiguration configuration,
    ITransactionRepository? transactions = null,
    ICustomerTransactionPresentation? presentation = null) : ControllerBase
{
    private IServiceClient Client => configuration.GetValue<bool>("Platform:Enabled") ? services.GetRequiredService<IServiceClient>() : throw new PlatformException(
        503,
        "platform_unavailable",
        "Financial services are temporarily unavailable.");

    private Task<JsonElement> Forward(string service, HttpMethod method, string path, object? body, CancellationToken ct) =>
        Client.SendServiceRequest<JsonElement>(service, method, path, body, User, ct);

    [HttpGet("country-catalog")]
    [AllowAnonymous]
    public IActionResult CountryCatalog() => Ok(CountryReferenceCatalog.All);

    [HttpGet("status")]
    [AllowAnonymous]
    public IActionResult Status() => Ok(new { enabled = configuration.GetValue<bool>("Platform:Enabled") });

    [HttpGet("settings")]
    [AllowAnonymous]
    public async Task<IActionResult> Settings(CancellationToken ct)
    {
        var settings = await Client.SendServiceRequest<PlatformSettings>("configuration", HttpMethod.Get, "/settings", ct: ct);
        var publication = settings.Publication;

        if (publication is not null)
        {
            settings = new global::DisputePortal.Platform.Contracts.PlatformSettings(settings)
            {
                Publication = new PolicyVersionMetadata
                {
                    PolicyPublicId = publication.PolicyPublicId,
                    VersionPublicId = publication.VersionPublicId,
                    Version = publication.Version,
                    ConcurrencyToken = publication.ConcurrencyToken,
                    EffectiveFromUtc = publication.EffectiveFromUtc,
                    EffectiveToUtc = publication.EffectiveToUtc,
                    EvaluationTimeUtc = publication.EvaluationTimeUtc,
                    NextChangeAtUtc = publication.NextChangeAtUtc,
                    SelectionReason = publication.SelectionReason,
                    State = publication.State
                }
            };
        }

        return Ok(settings);
    }

    [HttpPost("settings")]
    [Authorize(Policy = "operations.manage")]
    public async Task<IActionResult> Settings(SettingsUpdate r, CancellationToken ct) => Ok(await Forward(
        "configuration",
        HttpMethod.Post,
        "/settings",
        r,
        ct));

    [HttpGet("settings/history"), Authorize(Policy = "operations.manage")]
    public async Task<IActionResult> SettingsHistory(int page = 1, int pageSize = 20, string? search = null, CancellationToken ct = default) =>
        Ok(await Forward(
        "configuration",
        HttpMethod.Get,
        $"/settings/history?page={page}&pageSize={pageSize}&search={Uri.EscapeDataString(search ?? "")}",
        null,
        ct));

    [HttpPost("settings/preview"), Authorize(Policy = "operations.manage")]
    public async Task<IActionResult> SettingsPreview(SettingsUpdate request, CancellationToken ct) => Ok(await Forward(
        "configuration",
        HttpMethod.Post,
        "/settings/preview",
        request,
        ct));

    [HttpPost("settings/versions/{id:guid}/removal-preview"), Authorize(Policy = "operations.manage")]
    public async Task<IActionResult> SettingsRemovalPreview(Guid id, SettingsRemovalRequest request, CancellationToken ct) =>
        Ok(await Forward("configuration", HttpMethod.Post, $"/settings/versions/{id}/removal-preview", request, ct));

    [HttpPost("settings/versions/{id:guid}/remove"), Authorize(Policy = "operations.manage")]
    public async Task<IActionResult> SettingsRemove(Guid id, SettingsRemovalRequest request, CancellationToken ct) => Ok(await Forward(
        "configuration",
        HttpMethod.Post,
        $"/settings/versions/{id}/remove",
        request,
        ct));

    [HttpGet("accounts")]
    public async Task<IActionResult> Accounts(CancellationToken ct)
    {
        var accounts = await Client.SendServiceRequest<AccountRecord[]>("accounts", HttpMethod.Get, "/accounts", actor: User, ct: ct);
        var balances = await Client.SendServiceRequest<BalanceSnapshot[]>(
            "ledger",
            HttpMethod.Get,
            "/owners/" + User.UserId() + "/balances",
            actor: User,
            ct: ct);
        var index = balances.ToDictionary(b => b.AccountId);
        return Ok(accounts.Select(a => index.TryGetValue(a.Id, out var b) ? new global::DisputePortal.Platform.Contracts.AccountRecord(a)
        {
            LedgerBalance = b.LedgerBalance,
            ReservedBalance = b.ReservedBalance,
            Version = b.Version
        } : a));
    }

    [HttpPost("accounts")]
    public async Task<IActionResult> Accounts(OpenAccountRequest r, CancellationToken ct)
    {
        var portalSettings = await services.GetRequiredService<IPortalSettingsRepository>().GetPortalSettings(ct);
        if (!portalSettings.Settings.CustomerAccountOpeningEnabled)
        {
            throw new PortalForbiddenException("Account opening is currently unavailable. Contact support for assistance.");
        }

        return Ok(await Forward("accounts", HttpMethod.Post, "/accounts", r, ct));
    }

    [HttpGet("accounts/resolve/{number}")]
    public async Task<IActionResult> FindOwnedAccountByNumber(string number, CancellationToken ct)
    {
        var accounts = await Client.SendServiceRequest<AccountRecord[]>("accounts", HttpMethod.Get, "/accounts", actor: User, ct: ct);
        var account = accounts.SingleOrDefault(a => a.Number == number
            && a.Status == "Active") ?? throw new PlatformException(404, "account_missing", "Select one of your active accounts.");
        return Ok(new
        {
            id = account.Id,
            number = account.Number,
            currency = account.Currency
        });
    }

    [HttpGet("accounts/{id:guid}/statement")]
    public async Task<IActionResult> Statement(Guid id, CancellationToken ct) => Ok(await Forward(
        "accounts",
        HttpMethod.Get,
        $"/accounts/{id}/statement",
        null,
        ct));

    [HttpGet("transactions")]
    public async Task<IActionResult> Transactions(CancellationToken ct)
    {
        var rows = await Forward("transactions", HttpMethod.Get, "/transactions", null, ct);
        var details = transactions is null ? [] : (await transactions.GetTransactions(
            User.UserId(),
            new TransactionFilter
            {
                Page = 1,
                PageSize = 250,
                Search = null,
                From = null,
                To = null,
                Direction = null,
                Type = null,
                Sort = "date_desc",
                AccountId = null
            },
            ct)).Items;
        var indexed = details.ToDictionary(t => t.Id);
        var display = await (presentation ?? CustomerTransactionPresentation.Default).LoadTransactionPresentationRules(ct);
        var result = new JsonArray();
        foreach (var row in rows.EnumerateArray())
        {
            var value = JsonNode.Parse(row.GetRawText())!.AsObject();
            var type = row.GetProperty("kind").GetString() ?? "";
            indexed.TryGetValue(row.GetProperty("id").GetGuid(), out var detail);
            var raw = row.TryGetProperty("description", out var description) ? description.GetString() : null;
            value["description"] = detail?.Description ?? display.Description(type, null, raw);
            value["typeLabel"] = detail?.TypeLabel ?? display.TypeLabel(type);
            value["merchantName"] = detail?.MerchantName;
            result.Add(value);
        }

        return Ok(result);
    }

    [HttpPost("payments/quote")]
    public async Task<IActionResult> Quote(PaymentQuoteRequest r, CancellationToken ct)
    {
        RequireTransfer(r.Kind);
        await RequireOwnAccounts(r.AccountId, r.RecipientAccountId, ct);
        return Ok(await Forward("payments", HttpMethod.Post, "/quotes", r, ct));
    }

    [HttpPost("payments")]
    public async Task<IActionResult> Pay(PaymentRequest r, CancellationToken ct)
    {
        RequireTransfer(r.Kind);
        await RequireOwnAccounts(r.AccountId, r.RecipientAccountId, ct);
        return Ok(await Forward("payments", HttpMethod.Post, "/payments", r, ct));
    }

    [HttpGet("payments/requests/{id:guid}")]
    public async Task<IActionResult> RequestStatus(Guid id, CancellationToken ct) => Ok(await Forward(
        "payments",
        HttpMethod.Get,
        $"/payments/requests/{id}",
        null,
        ct));

    [HttpPost("payments/requests/{id:guid}/cancel")]
    public async Task<IActionResult> CancelUnknown(Guid id, CancellationToken ct) => Ok(await Forward(
        "payments",
        HttpMethod.Post,
        $"/payments/requests/{id}/cancel",
        null,
        ct));

    [HttpGet("payments")]
    public async Task<IActionResult> Payments(CancellationToken ct) =>
        Ok(await Forward("payments", HttpMethod.Get, "/payments", null, ct));

    [HttpGet("notifications")]
    public async Task<IActionResult> Notifications(CancellationToken ct) => Ok(await Forward(
        "notifications",
        HttpMethod.Get,
        "/notifications",
        null,
        ct));

    [HttpPost("notifications/{id:guid}/read")]
    public async Task<IActionResult> MarkNotificationRead(Guid id, CancellationToken ct)
    {
        await Client.SendServiceRequest<object>("notifications", HttpMethod.Post, $"/notifications/{id}/read", actor: User, ct: ct);
        return NoContent();
    }

    [HttpGet("reports")]
    public async Task<IActionResult> Reports(CancellationToken ct) => Ok(await Forward("reporting", HttpMethod.Get, "/summary", null, ct));

    [HttpGet("reconciliation")]
    public async Task<IActionResult> Reconcile(CancellationToken ct) => Ok(await Forward(
        "ledger",
        HttpMethod.Get,
        "/reconciliation",
        null,
        ct));

    [HttpGet("audit")]
    public async Task<IActionResult> Audit(Guid? correlation, CancellationToken ct) => Ok(await Forward(
        "audit",
        HttpMethod.Get,
        "/events" + (correlation is { } id ? "?correlation=" + id : ""),
        null,
        ct));

    [HttpGet("reversals")]
    public async Task<IActionResult> Reversals(CancellationToken ct) => Ok(DescribeReviews(await Forward(
        "scheduling",
        HttpMethod.Get,
        "/reversals",
        null,
        ct)));

    [HttpPost("reversals")]
    public async Task<IActionResult> Reversals(ScheduleReversal r, CancellationToken ct) => Ok(await Forward(
        "scheduling",
        HttpMethod.Post,
        "/reversals",
        r,
        ct));

    [HttpGet("operations/{service}/delivery")]
    public async Task<IActionResult> Delivery(string service, CancellationToken ct)
    {
        CheckService(service);
        return Ok(await Forward(service, HttpMethod.Get, "/operations/delivery", null, ct));
    }

    [HttpPost("operations/{service}/deadletters/replay")]
    public async Task<IActionResult> Replay(string service, ReplayDeadLetters request, CancellationToken ct)
    {
        CheckService(service);
        return Ok(await Forward(service, HttpMethod.Post, "/operations/deadletters/replay", request, ct));
    }

    [HttpGet("accounts/{id:guid}/transfer-targets")]
    public async Task<IActionResult> TransferTargets(Guid id, CancellationToken ct)
    {
        var accounts = await Client.SendServiceRequest<AccountRecord[]>("accounts", HttpMethod.Get, "/accounts", actor: User, ct: ct);
        return Ok(EligibleTransferTargets(id, accounts));
    }

    private static IEnumerable<AccountRecord> EligibleTransferTargets(Guid source, AccountRecord[] accounts)
    {
        var origin = accounts.SingleOrDefault(account => account.Id == source && account.Status == "Active");
        return origin is null ? [] : accounts.Where(account => account.Id != source && account.Status == "Active" && account.Currency == origin.Currency);
    }

    private async Task RequireOwnAccounts(Guid source, Guid? recipient, CancellationToken ct)
    {
        var accounts = await Client.SendServiceRequest<AccountRecord[]>("accounts", HttpMethod.Get, "/accounts", actor: User, ct: ct);
        if (recipient is null || !EligibleTransferTargets(source, accounts).Any(account => account.Id == recipient))
        {
            throw new PlatformException(
                400,
                "own_account_required",
                "Choose two different active accounts in the same currency belonging to your profile.");
        }
    }

    private static void RequireTransfer(string kind)
    {
        if (kind != "Transfer")
        {
            throw new PlatformException(
                409,
                "external_settlement_required",
                "Deposits and withdrawals are recorded only through the external bank integration.");
        }
    }

    private void CheckService(string name)
    {
        if (!new[]
        {
            "configuration",
            "accounts",
            "ledger",
            "transactions",
            "payments",
            "notifications",
            "audit",
            "reporting",
            "scheduling",
            "debt"
        }.Contains(name))
        {
            throw new PlatformException(404, "service_missing", "Service not found.");
        }
    }

    [HttpGet("debt/reviews")]
    public async Task<IActionResult> Reviews(CancellationToken ct) => Ok(DescribeReviews(await Forward(
        "debt",
        HttpMethod.Get,
        "/reviews",
        null,
        ct)));

    private JsonArray DescribeReviews(JsonElement rows)
    {
        var result = JsonNode.Parse(rows.GetRawText())!.AsArray();
        foreach (var row in result.OfType<JsonObject>())
            row["outcomeMessage"] = ReviewOutcomeMessages.Describe(
                configuration,
                row["status"]?.GetValue<string>() ?? "",
                row["decisionCode"]?.GetValue<string>());
        return result;
    }
}
