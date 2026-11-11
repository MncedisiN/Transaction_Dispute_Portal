using Dapper;
using DisputePortal.Api.Models.Exceptions;
using DisputePortal.Api.Models.Requests;
using DisputePortal.Api.Models.Responses;
using System.Text.Json;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Interfaces.Data;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Interfaces.Security;
using DisputePortal.Platform.Infrastructure.Sql;

namespace DisputePortal.Api.Repository;

public class ProfileRepository(
    IDatabaseConnectionFactory factory,
    IDatabaseDialect databaseDialect,
    TimeZoneInfo businessTimeZone,
    ISecretProtector secretProtector,
    IOtpHasher otpHasher) : RepositoryBase(factory, databaseDialect), IProfileRepository
{
    public async Task<ProfileDto> GetProfile(Guid user, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        var sql = SqlScripts.RenderSqlScript(
            "Shared/Queries/Api/ProfileRepository/GetUsers",
            ("DateTimeProjection", dialect.DateTimeProjection("p.\"dateOfBirth\"")));
        var profiles = await c.QueryAsync<ProfileDto, string?, ProfileDto>(
            new CommandDefinition(sql, new { Id = user }, cancellationToken: ct),
            (profile, encrypted) =>
        {
            profile.Address = encrypted is null ? null : JsonSerializer.Deserialize<ResidentialAddress>(secretProtector.Unprotect(encrypted));
            return profile;
        },
            splitOn: "ProtectedAddress");
        return profiles.SingleOrDefault() ?? throw new PortalNotFoundException();
    }

    public async Task<string> RevealIdentityNumber(Guid user, CancellationToken ct)
    {
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var encrypted = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/ProfileRepository/GetActiveCustomerProfileForUser"),
            new { User = user },
            tx,
            cancellationToken: ct));
        if (string.IsNullOrWhiteSpace(encrypted))
        {
            throw new PortalNotFoundException();
        }

        var identity = secretProtector.Unprotect(encrypted);
        await AuditOperation(c, tx, user, "IdentityNumberRevealed", user, new { }, ct);
        await tx.CommitAsync(ct);
        return identity;
    }

    public async Task UpdateProfileDetails(Guid user, ProfileDetailsRequest request, CancellationToken ct)
    {
        // MVC validates request properties; repository callers also need validation.
        static bool RequiredText(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;
        static bool OptionalText(string? value, int maximum) => value is null || value.Length <= maximum;
        if (!RequiredText(request.FirstName, 100) || !RequiredText(request.LastName, 100) || !OptionalText(request.MiddleName, 100))
        {
            throw new PortalValidationException("Enter your first name and surname, using up to 100 characters each.");
        }

        if (request.Address is { } address
            && (!RequiredText(address.Line1, 200)
            || !RequiredText(address.City, 100)
            || !OptionalText(address.Line2, 200)
            || !OptionalText(address.Suburb, 100)
            || !OptionalText(address.Province, 100)
            || !OptionalText(address.PostalCode, 20)
            || address.CountryCode is not { Length: 2 }
            || address.CountryCode.Any(c => c is < 'A' or > 'Z')))
        {
            throw new PortalValidationException("Enter a complete street address, city and two-letter country code.");
        }

        var name = request.FirstName.Trim() + " " + request.LastName.Trim();
        if (name.Length > 200)
        {
            throw new PortalValidationException("Keep your combined name within 200 characters.");
        }

        var protectedAddress = request.Address is null ? null : secretProtector.Protect(JsonSerializer.Serialize(request.Address));
        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var count = await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/ProfileRepository/UpdateCustomerProfiles"),
            new
            {
                Id = user,
                Name = name,
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                MiddleName = string.IsNullOrWhiteSpace(request.MiddleName) ? null : request.MiddleName.Trim(),
                Address = protectedAddress,
                Now = DateTimeOffset.UtcNow
            },
            tx,
            cancellationToken: ct));
        if (count != 1)
        {
            throw new PortalNotFoundException();
        }

        await AuditOperation(c, tx, user, "ProfileDetailsChanged", user, new { AddressProvided = request.Address != null }, ct);
        await tx.CommitAsync(ct);
    }

    public async Task SetIdentity(Guid user, string identityNumber, int birthCentury, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, businessTimeZone).Date);
        if (!SouthAfricanIdentity.TryGetBirthDate(identityNumber, birthCentury, today, out var birthDate))
        {
            throw new PortalValidationException("Check the South African ID number and date of birth.");
        }

        await using var c = await connections.OpenConnection(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await LockOperations(c, tx, ct);
        // The conditional write makes first-write-only identity storage safe under concurrent requests.
        var changed = await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/ProfileRepository/UpdateCustomerProfilesForUser"),
            new
            {
                User = user,
                ProtectedIdentity = secretProtector.Protect(identityNumber),
                LastFour = identityNumber[^4..],
                BirthDate = birthDate.ToDateTime(TimeOnly.MinValue),
                Now = DateTimeOffset.UtcNow
            },
            tx,
            cancellationToken: ct));
        if (changed != 1)
        {
            throw new PortalConflictException("Identity details are already saved and cannot be changed here.");
        }

        var identityHash = otpHasher.Hash(Guid.Empty, "demo-bank-identity:" + identityNumber);
        if (await c.ExecuteScalarAsync<int>(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/ProfileRepository/CountLocalBankingIdentitiesForUser"),
            new { Hash = identityHash, User = user },
            tx,
            cancellationToken: ct)) > 0)
        {
            throw new PortalConflictException("This identity is already linked to another customer profile.");
        }

        await c.ExecuteAsync(new CommandDefinition(
            SqlScripts.ReadSqlScript("Shared/Queries/Api/ProfileRepository/UpdateLocalBankingIdentitiesForUser"),
            new { Hash = identityHash, User = user },
            tx,
            cancellationToken: ct));
        await AuditOperation(c, tx, user, "IdentityDetailsRecorded", user, new { }, ct);
        await tx.CommitAsync(ct);
    }
}
