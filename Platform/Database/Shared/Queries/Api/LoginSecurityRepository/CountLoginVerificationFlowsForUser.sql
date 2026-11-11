SELECT
    COUNT(*)
FROM "identity"."loginVerificationFlows"
WHERE "userId" = (
    SELECT
        "users"."id" AS "row_id"
    FROM "identity"."users" AS "users"
    WHERE "users"."publicId" = @User)
AND "createdAt" > @Since
