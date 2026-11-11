using System.Text.RegularExpressions;
using Dapper;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Security;
using DisputePortal.Platform.Infrastructure.Interfaces;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Platform;

public class ProfileDocumentService(IDatabaseConnectionFactory connections, ISecretProtector protector, IServiceClient client)
{
    public async Task SaveIdentityDocument(Guid user, ProfileDocumentRequest request, CancellationToken ct)
    {
        var settings = await client.SendServiceRequest<PlatformSettings>("configuration", HttpMethod.Get, "/settings", ct: ct);
        var issuer = settings.Countries.SingleOrDefault(c => c.Code == request.IssuerCountry
            && c.Active) ?? throw new PortalValidationException("Select a supported document issuer.");
        var doc = issuer.Documents.SingleOrDefault(d => d.Key == request.DocumentType) ?? throw new PortalValidationException("Select a document accepted for the issuer.");
        var number = request.DocumentNumber.Trim().ToUpperInvariant();
        if (!Regex.IsMatch(number, doc.Pattern, RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(50)))
        {
            throw new PortalValidationException("Check the document number against the selected issuer's format.");
        }

        var birth = request.DateOfBirth;
        if (doc.Validator == "south-african-id")
        {
            if (issuer.Code != "ZA"
                || !SouthAfricanIdentity.TryGetBirthDate(
                number,
                request.BirthCentury ?? SouthAfricanIdentity.InferCentury(number, DateOnly.FromDateTime(DateTime.UtcNow)),
                DateOnly.FromDateTime(DateTime.UtcNow),
                out var derived))
            {
                throw new PortalValidationException("Check the ID number and birth year.");
            }

            birth = derived;
        }

        if (birth is { } date && (date > DateOnly.FromDateTime(DateTime.UtcNow) || date.Year < 1900))
        {
            throw new PortalValidationException("Check the date of birth.");
        }

        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await RelationalDialect.LockOperations(
            c,
            tx,
            RelationalDialect.GetDatabaseDialect(connections.Provider.ToString()).OperationsLockSql,
            ct);
        var changed = await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/ProfileDocumentService/UpdateCustomerProfilesForUser"),
            new
            {
                User = user,
                Protected = protector.Protect(number),
                LastFour = number.Length >= 4 ? number[^4..] : number,
                Birth = birth?.ToDateTime(TimeOnly.MinValue),
                Type = doc.Key,
                Issuer = issuer.Code,
                Now = DateTimeOffset.UtcNow
            },
            tx,
            cancellationToken: ct));
        if (changed != 1)
        {
            throw new PortalConflictException("Identity details are already saved and cannot be changed here.");
        }

        await ServiceDatabase.InsertServiceAuditEvent(
            c,
            tx,
            user,
            "ProfileDocumentRecorded",
            user,
            "Configured issuer and document validation applied; document content is encrypted.",
            ct);
        await tx.CommitAsync(ct);
    }
}
