UPDATE "identity"."users"
SET
    "loginIdentifier" = @Destination,
    "normalizedLoginIdentifier" = @Normalized,
    "updatedAt" = @Now
WHERE "users"."publicId" = @User
AND EXISTS (
    SELECT
        1
    FROM "identity"."customerProfiles" own_profile
    WHERE (
        SELECT
            "related1"."publicId"
        FROM "identity"."users" AS "related1"
        WHERE "related1"."id" = "own_profile"."userId") = @User
    AND "own_profile"."contactLoginEnabled" = {{TrueValue}});

UPDATE "identity"."customerProfiles"
SET
    "email" = @Destination,
    "emailVerified" = {{RelatedTrueValue}},
    "preferredOtpChannel" = 'email',
    "maskedEmail" = @Masked,
    "updatedAt" = @Now
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "customerProfiles"."userId") = @User
