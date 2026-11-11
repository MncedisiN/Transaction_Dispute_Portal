SELECT
    (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "transaction_disputes"."customerId") AS "customer_id"
FROM "disputes"."disputes" AS "transaction_disputes"
WHERE "transaction_disputes"."publicId" = @Id
