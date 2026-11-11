SELECT
    (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "support_threads"."customerId") AS "customer_id"
FROM "support"."conversations" AS "support_threads"
WHERE "support_threads"."publicId" = @Thread
