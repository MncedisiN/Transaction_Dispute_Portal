UPDATE "support"."staffCapacity"
SET
    "lastAssignedAt" = @Now
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "staffCapacity"."userId") = @User
