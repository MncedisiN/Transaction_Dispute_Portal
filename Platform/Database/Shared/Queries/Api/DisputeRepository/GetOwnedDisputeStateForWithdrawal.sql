SELECT
    "transaction_disputes"."status" AS Status,
    "transaction_disputes"."version" AS Version,
    (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "transaction_disputes"."customerId") AS CustomerId
FROM "disputes"."disputes" AS "transaction_disputes"
WHERE "transaction_disputes"."publicId" = @Id
AND(
    SELECT
        "related2"."publicId"
    FROM "identity"."users" AS "related2"
    WHERE "related2"."id" = "transaction_disputes"."customerId") = @Customer
