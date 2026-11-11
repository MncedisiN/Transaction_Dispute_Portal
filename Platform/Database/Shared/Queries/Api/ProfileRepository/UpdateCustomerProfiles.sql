UPDATE "identity"."customerProfiles"
SET
    "displayName" = @Name,
    "firstName" = @FirstName,
    "lastName" = @LastName,
    "middleName" = @MiddleName,
    "protectedAddress" = @Address,
    "updatedAt" = @Now
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "customerProfiles"."userId") = @Id
AND EXISTS (
    SELECT
        1
    FROM "identity"."users" u
    WHERE "u"."publicId" = @Id
    AND "u"."accountStatus" = 'Active')
