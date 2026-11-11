UPDATE "identity"."trustedLoginDevices"
SET
    "revokedAt" = @Now
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "trustedLoginDevices"."userId") = @User
AND "trustedLoginDevices"."revokedAt" IS NULL
AND(@Id IS NULL
    OR "trustedLoginDevices"."publicId" = @Id)
