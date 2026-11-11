SELECT
    "transaction_disputes"."status" AS "status"
FROM "disputes"."disputes" AS "transaction_disputes"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "transactions"."transactions" AS "related1"
    WHERE "related1"."id" = "transaction_disputes"."transactionId") = @Id
ORDER BY "transaction_disputes"."createdAt" DESC, "transaction_disputes"."id" DESC
OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY
