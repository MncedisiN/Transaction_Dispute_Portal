SELECT
    "transaction_disputes"."publicId" AS "id",
    (
    SELECT
        "related1"."publicId"
    FROM "transactions"."transactions" AS "related1"
    WHERE "related1"."id" = "transaction_disputes"."transactionId") AS TransactionId,
    "transaction_disputes"."status" AS "status"
FROM "disputes"."disputes" AS "transaction_disputes"
WHERE "transaction_disputes"."publicId" = @Id
AND(
    SELECT
        "related2"."publicId"
    FROM "identity"."users" AS "related2"
    WHERE "related2"."id" = "transaction_disputes"."customerId") = @Actor
