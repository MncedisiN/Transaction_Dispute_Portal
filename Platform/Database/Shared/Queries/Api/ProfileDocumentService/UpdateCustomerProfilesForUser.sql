UPDATE "identity"."customerProfiles"
SET
    "protectedIdentityNumber" = @Protected,
    "identityLastFour" = @LastFour,
    "dateOfBirth" = @Birth,
    "identityDocumentType" = @Type,
    "identityIssuerCountry" = @Issuer,
    "updatedAt" = @Now
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "customerProfiles"."userId") = @User
AND "customerProfiles"."protectedIdentityNumber" IS NULL
AND EXISTS (
    SELECT
        1
    FROM "identity"."users" AS "users"
    WHERE "users"."publicId" = @User
    AND "users"."accountStatus" = 'Active')
