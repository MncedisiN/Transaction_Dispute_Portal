UPDATE "identity"."customerProfiles"
SET
    "residenceCountry" = @CountryCode,
    "phoneCountry" = @PhoneCountry,
    "identityDocumentType" = @DocumentType,
    "identityIssuerCountry" = @DocumentIssuer,
    "accountCurrency" = @AccountCurrency,
    "protectedIdentityNumber" = @Identity,
    "identityLastFour" = @LastFour,
    "dateOfBirth" = @BirthDate,
    "protectedAddress" = @Address
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "customerProfiles"."userId") = @Id
