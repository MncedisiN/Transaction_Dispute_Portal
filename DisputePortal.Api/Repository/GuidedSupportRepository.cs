using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;

public partial class SupportRepository
{
    private static readonly Regex CredentialValue = new(
        @"\b(?:password|passcode)\s*[:=]\s*\S+|\b(?:pin|otp|cvv|cvc)\s*[:=]\s*\d{3,8}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(100));
    private static readonly Regex CardCandidate = new(
        @"\b(?:\d[ -]?){13,19}\b",
        RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(100));

    internal static string? EnquiryRequestHash(SupportSendRequest request)
    {
        try
        {
            if (CredentialValue.IsMatch(request.Body)
                || CardCandidate.Matches(request.Body).Any(match => IsCardNumber(match.Value)))
            {
                throw new PortalValidationException("Remove passwords, security codes and full card numbers before submitting. Use the secure transaction selector instead.");
            }
        }
        catch (RegexMatchTimeoutException)
        {
            throw new PortalValidationException("This message could not be checked safely. Please try again.");
        }

        if (request.Enquiry is null)
        {
            if (request.StartNewConversation)
            {
                throw new PortalValidationException("Review a complete enquiry before starting a new conversation.");
            }

            return null;
        }

        var details = request.Enquiry;
        if (request.ThreadId.HasValue
            || request.ClientMessageId is null
            || request.ClientMessageId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.ConcernCode))
        {
            throw new PortalValidationException("Choose a concern and submit the completed enquiry once.");
        }

        if (string.IsNullOrWhiteSpace(details.Description)
            || details.Description.Trim().Length is < 2 or > 2000
            || details.AdditionalDetails?.Length > 1000
            || details.MessageBody.Length > 2000
            || request.Body.Trim() != details.MessageBody)
        {
            throw new PortalValidationException("Review your explanation and keep the complete enquiry within 2,000 characters.");
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, JsonSerializerOptions.Web))));
    }

    private static bool IsCardNumber(string candidate)
    {
        var digits = candidate.Where(char.IsAsciiDigit).Select(c => c - '0').ToArray();
        if (digits.Length is < 13 or > 19 || digits[0] is not (3 or 4 or 5 or 6))
        {
            return false;
        }

        var sum = 0;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var value = digits[i];
            if ((digits.Length - 1 - i) % 2 == 1)
            {
                value *= 2;
                if (value > 9)
                {
                    value -= 9;
                }
            }

            sum += value;
        }

        return sum % 10 == 0;
    }

    private async Task<(SupportSendRequest Request, EnquirySnapshot Snapshot)> PrepareGuidedEnquiry(
        DbConnection connection,
        DbTransaction transaction,
        Guid actor,
        SupportSendRequest request,
        CancellationToken ct)
    {
        if (!(await Roles(connection, transaction, actor, ct)).Contains("customer"))
        {
            throw new PortalForbiddenException("Use a customer workspace to submit a personal enquiry.");
        }

        var concern = await connection.QuerySingleOrDefaultAsync<GuidedConcern>(new CommandDefinition(
            SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/SupportRepository/GetEnabledConcernEnquiryRequirements",
            ("TrueValue", dialect.TrueLiteral)),
            new { Code = request.ConcernCode },
            transaction,
            cancellationToken: ct)) ?? throw new PortalValidationException("Choose an available concern type.");
        if (request.DisputeId.HasValue && request.RelatedDisputeId.HasValue)
        {
            throw new PortalValidationException("Select one related case.");
        }

        if (request.DisputeId.HasValue || request.RelatedDisputeId.HasValue)
        {
            var id = request.DisputeId ?? request.RelatedDisputeId;
            var related = await connection.QuerySingleOrDefaultAsync<GuidedCase>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/SupportRepository/GetOwnedRelatedCaseContext"),
                new { Id = id, Actor = actor },
                transaction,
                cancellationToken: ct)) ?? throw new PortalNotFoundException();
            if (request.TransactionId.HasValue && request.TransactionId != related.TransactionId)
            {
                throw new PortalValidationException("Choose the transaction belonging to this case.");
            }

            request = DisputeWorkflow.CanSendCaseMessage(related.Status) ? new SupportSendRequest(request)
            {
                DisputeId = related.Id,
                RelatedDisputeId = null,
                TransactionId = related.TransactionId,
                StartNewConversation = false
            }

            : new SupportSendRequest(request)
            {
                DisputeId = null,
                RelatedDisputeId = related.Id,
                TransactionId = null,
                StartNewConversation = true
            };
        }
        else if (request.TransactionId is { } transactionId)
        {
            var related = await connection.QuerySingleOrDefaultAsync<GuidedTransaction>(new CommandDefinition(
                SqlScripts.ReadSqlScript("Shared/Queries/Api/SupportRepository/GetOwnedRelatedTransactionContext"),
                new { Id = transactionId, Actor = actor },
                transaction,
                cancellationToken: ct)) ?? throw new PortalNotFoundException();
            if (related.DisputeId is { } previous && !DisputeWorkflow.CanSendCaseMessage(related.CaseStatus))
            {
                request = new SupportSendRequest(request)
                {
                    TransactionId = null,
                    RelatedDisputeId = previous,
                    StartNewConversation = true
                };
            }
        }

        var hasCase = request.DisputeId.HasValue || request.RelatedDisputeId.HasValue;
        if ((concern.RequiresCase || concern.ContextKind == "Case") && !hasCase)
        {
            throw new PortalValidationException("Select the dispute related to this enquiry.");
        }

        if (concern.ContextKind == "Transaction" && !request.TransactionId.HasValue && !hasCase)
        {
            throw new PortalValidationException("Select the transaction related to this enquiry.");
        }

        if (request.StartNewConversation && !request.RelatedDisputeId.HasValue)
        {
            throw new PortalValidationException("Start a new case conversation from a closed case.");
        }

        var details = request.Enquiry!;
        if (details.OccurredOn is { } occurred
            && occurred > DateOnly.FromDateTime((timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime).AddDays(1))
        {
            throw new PortalValidationException("Check when the issue occurred.");
        }

        var snapshot = new EnquirySnapshot
        {
            ConcernCode = concern.Code,
            ConcernLabel = concern.Label,
            Description = details.Description.Trim(),
            AdditionalDetails = string.IsNullOrWhiteSpace(details.AdditionalDetails) ? null : details.AdditionalDetails.Trim(),
            OccurredOn = details.OccurredOn
        };
        return (request, snapshot);
    }

    private class GuidedConcern
    {
        public string Code { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public bool RequiresCase { get; set; }
        public string ContextKind { get; set; } = string.Empty;
        public bool CollectDate { get; set; }

        public GuidedConcern()
        {
        }

        public GuidedConcern(string Code, string Label, bool RequiresCase, string ContextKind, bool CollectDate)
        {
            this.Code = Code;
            this.Label = Label;
            this.RequiresCase = RequiresCase;
            this.ContextKind = ContextKind;
            this.CollectDate = CollectDate;
        }
    }

    private class GuidedCase
    {
        public Guid Id { get; set; }
        public Guid TransactionId { get; set; }
        public string Status { get; set; } = string.Empty;

        public GuidedCase()
        {
        }

        public GuidedCase(Guid Id, Guid TransactionId, string Status)
        {
            this.Id = Id;
            this.TransactionId = TransactionId;
            this.Status = Status;
        }
    }

    private class GuidedTransaction
    {
        public Guid Id { get; set; }
        public Guid? DisputeId { get; set; }
        public string? CaseStatus { get; set; }

        public GuidedTransaction()
        {
        }

        public GuidedTransaction(Guid Id, Guid? DisputeId, string? CaseStatus)
        {
            this.Id = Id;
            this.DisputeId = DisputeId;
            this.CaseStatus = CaseStatus;
        }
    }
}
