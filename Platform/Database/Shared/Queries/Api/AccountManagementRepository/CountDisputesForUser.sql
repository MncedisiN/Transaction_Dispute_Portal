SELECT
    COUNT(*)
FROM "disputes"."disputes" AS "transaction_disputes"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "transaction_disputes"."customerId") = @User
AND "transaction_disputes"."status" NOT IN ('Resolved', 'Declined', 'Withdrawn')
