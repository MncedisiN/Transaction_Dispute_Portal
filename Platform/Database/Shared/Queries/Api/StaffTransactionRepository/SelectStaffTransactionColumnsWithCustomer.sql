(
    SELECT
        "rr"."customerReference" AS "customer_reference"
    FROM "disputes"."refunds" r
    JOIN "disputes"."refundReceipts" rr ON "rr"."refundId" = "r"."id"
    WHERE "r"."creditTransactionId" = "t"."id")
