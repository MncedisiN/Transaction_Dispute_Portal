using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Globalization;
using Dapper;
using DisputePortal.Api.Repository;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/portal/reference")]
[EnableRateLimiting("operations-read")]
public class PortalReferenceController(
    IDatabaseConnectionFactory connections,
    IPortalSettingsRepository settings,
    ITransactionRepository transactions,
    CaseStatusRepository statuses,
    IEvidencePolicyRepository evidencePolicies) : ControllerBase
{
    // Public reference descriptions contain no customer, credential or account data.
    [HttpGet]
    public async Task<IActionResult> GetPortalReferenceData(CancellationToken ct)
    {
        var version = await settings.GetPortalSettings(ct);
        var descriptions = version.Settings;
        var catalogue = await statuses.GetWorkflowStatuses(ct);
        var evidencePolicy = await evidencePolicies.GetEvidencePolicy(ct);
        var statusDescriptions = catalogue.Items.ToDictionary(
            row => row.Code,
            row => new
            {
                Label = row.DisplayName,
                row.CustomerLabel,
                row.Tone,
                row.Stage,
                row.NextStep,
                row.OwnerLabel,
                row.Steps,
                row.Closed,
                row.SortOrder
            });
        await using var connection = await connections.OpenConnection(ct);
        var codes = await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT
                code
            FROM "identity"."roles"
            ORDER BY code
            """,
            cancellationToken: ct));
        var roles = codes.ToDictionary(
            code => code,
            code =>
        {
            var description = descriptions.Roles.GetValueOrDefault(code);
            return new
            {
                Rank = description?.Rank ?? int.MaxValue,
                Label = description?.Label ?? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(code.Replace('_', ' ')),
                Description = description?.Description ?? string.Empty,
                Assignable = CaseServicePolicy.IsSpecialist(code) || CaseServicePolicy.IsSupervisor(code),
                Specialist = CaseServicePolicy.IsSpecialist(code)
            };
        });
        var queues = CaseServicePolicy.Queues.ToDictionary(
            code => code,
            code =>
        {
            var value = descriptions.Queues.GetValueOrDefault(code) ?? throw new InvalidOperationException("A queue description is missing.");
            return new
            {
                value.Label,
                value.Steps,
                Role = CaseServicePolicy.SpecialistFor(code)
            };
        });
        var transactionTypes = (await transactions.GetTransactionTypes(ct)).ToDictionary(type => type.TypeKey, type => type.Label);
        var durations = evidencePolicy.AccessDurations.Where(pair => int.TryParse(pair.Key, out var hours)
            && hours >= evidencePolicy.MinimumAccessHours && hours <= evidencePolicy.MaximumAccessHours).ToDictionary(
            pair => pair.Key,
            pair => pair.Value);
        if (!durations.ContainsKey(evidencePolicy.DefaultAccessHours.ToString(CultureInfo.InvariantCulture)))
        {
            throw new InvalidOperationException("Evidence access settings are incomplete.");
        }

        return Ok(new
        {
            Version = version.Version,
            descriptions.ClientOptions,
            descriptions.CustomerSearchEnabled,
            descriptions.CustomerAccountOpeningEnabled,
            Statuses = statusDescriptions,
            Roles = roles,
            Queues = queues,
            descriptions.Priorities,
            descriptions.EnquiryFlow,
            descriptions.Severities,
            TransactionTypes = transactionTypes,
            descriptions.TransactionStatuses,
            descriptions.PaymentStatuses,
            descriptions.Availability,
            descriptions.StaffStatuses,
            AssignmentReason = Length<AssignmentRequest>(nameof(AssignmentRequest.Reason)),
            EscalationReason = Length<EscalationRequest>(nameof(EscalationRequest.Reason)),
            PolicyReason = Length<ResetDisputePoliciesRequest>(nameof(ResetDisputePoliciesRequest.Reason)),
            EvidenceDurations = durations,
            EvidenceDefaultHours = evidencePolicy.DefaultAccessHours,
            Evidence = new
            {
                Categories = evidencePolicy.Categories.Select(category => new
                {
                    Value = category.Code,
                    category.Label
                }),
                evidencePolicy.MaximumFiles,
                evidencePolicy.MaximumFileBytes,
                evidencePolicy.MaximumOriginalBytes,
                evidencePolicy.MaximumCaseBytes,
                AllowedExtensions = evidencePolicy.Formats.Keys,
                UploadStatuses = catalogue.Items
                    .Where(row => DisputeWorkflow.CanSendCaseMessage(row.Code))
                    .Select(row => row.Code)
            },
            EvidenceReason = new
            {
                Minimum = evidencePolicy.AccessReasonMinimumLength,
                Maximum = evidencePolicy.AccessReasonMaximumLength
            },
            SupportBody = Length<SupportSendRequest>(nameof(SupportSendRequest.Body)),
            SupportSubject = Length<SupportSendRequest>(nameof(SupportSendRequest.Subject)),
            AdditionalDetails = Length<EnquiryDetails>(nameof(EnquiryDetails.AdditionalDetails)),
            FinancialReason = new
            {
                Minimum = RefundRecallPolicy.ReasonMinimumLength,
                Maximum = RefundRecallPolicy.ReasonMaximumLength
            },
            StaffCapacity = Range<UpdateUserRequest>(nameof(UpdateUserRequest.Capacity)),
            Password = Length<SaveUserRequest>(nameof(SaveUserRequest.Password))
        });
    }

    private static object Length<T>(string property)
    {
        var info = typeof(T).GetProperty(property)!;
        var parameter = typeof(T).GetConstructors().SelectMany(constructor => constructor.GetParameters()).FirstOrDefault(value => string.Equals(
            value.Name,
            property,
            StringComparison.OrdinalIgnoreCase));
        TAttribute? Attribute<TAttribute>()
            where TAttribute : System.Attribute => info.GetCustomAttribute<TAttribute>() ?? parameter?.GetCustomAttribute<TAttribute>();
        var length = Attribute<StringLengthAttribute>();
        return new
        {
            Minimum = Attribute<MinLengthAttribute>()?.Length ?? length?.MinimumLength ?? 0,
            Maximum = Attribute<MaxLengthAttribute>()?.Length ?? length?.MaximumLength
        };
    }

    private static object Range<T>(string property)
    {
        var range = typeof(T).GetProperty(property)!.GetCustomAttribute<RangeAttribute>()!;
        return new
        {
            Minimum = range.Minimum,
            Maximum = range.Maximum
        };
    }

}
