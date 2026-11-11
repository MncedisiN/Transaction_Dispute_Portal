UPDATE "identity"."disputeVerifications"
SET
    "state" = 'Expired'
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "disputeVerifications"."userId") = @User
AND(
    SELECT
        "related2"."publicId"
    FROM "transactions"."transactions" AS "related2"
    WHERE "related2"."id" = "disputeVerifications"."transactionId") = @Transaction
AND "disputeVerifications"."state" IN ('Pending', 'Ready', 'Verified')
