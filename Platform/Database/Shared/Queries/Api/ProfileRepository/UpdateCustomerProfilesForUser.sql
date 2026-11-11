UPDATE "identity"."customerProfiles"
SET
    "protectedIdentityNumber" = @ProtectedIdentity,
    "identityLastFour" = @LastFour,
    "dateOfBirth" = @BirthDate,
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
    FROM "identity"."users" u
    WHERE "u"."publicId" = @User
    AND "u"."accountStatus" = 'Active')
