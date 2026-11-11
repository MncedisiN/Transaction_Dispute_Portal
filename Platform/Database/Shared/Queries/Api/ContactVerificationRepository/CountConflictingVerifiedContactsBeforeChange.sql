SELECT
    COUNT(*)
FROM "identity"."users" AS "users"
WHERE "users"."normalizedLoginIdentifier" = @Email
AND "users"."publicId" <> @User
AND EXISTS (
    SELECT
        1
    FROM "identity"."customerProfiles" own_profile
    WHERE (
        SELECT
            "related1"."publicId"
        FROM "identity"."users" AS "related1"
        WHERE "related1"."id" = "own_profile"."userId") = @User
    AND "own_profile"."contactLoginEnabled" = {{TrueValue}})
