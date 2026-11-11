SELECT
    "transaction_disputes"."publicId" AS "id",
    (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "transaction_disputes"."customerId") AS CustomerId
FROM "disputes"."disputes" AS "transaction_disputes"
WHERE {{MembershipPredicate}}
