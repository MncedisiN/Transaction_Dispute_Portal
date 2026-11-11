SELECT
    COUNT(*)
FROM "identity"."disputeVerifications" AS "dispute_verifications"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "dispute_verifications"."userId") = @User
AND "dispute_verifications"."createdAt" > @Since
