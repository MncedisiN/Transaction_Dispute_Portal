UPDATE "identity"."disputeVerifications"
SET
    "state" = 'Ready'
WHERE "disputeVerifications"."publicId" = @Id
AND(
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "disputeVerifications"."userId") = @User
AND "disputeVerifications"."state" = 'Pending'
