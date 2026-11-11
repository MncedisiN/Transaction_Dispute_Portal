UPDATE "identity"."customerProfiles"
SET
    "firstName" = @FirstName,
    "middleName" = @MiddleName,
    "lastName" = @LastName,
    "displayName" = @Name,
    "updatedAt" = @Now
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "customerProfiles"."userId") = @Id
