SELECT
    "t"."publicId" AS "id",
    (
    SELECT
        "related1"."publicId"
    FROM "accounts"."accounts" AS "related1"
    WHERE "related1"."id" = "t"."accountId") AS AccountId,
    "a"."accountType" AS AccountType,
    CASE WHEN(
    SELECT
        "related2"."code"
    FROM "configuration"."transactionTypes" AS "related2"
    WHERE "related2"."id" = "t"."transactionTypeId") = 'Refund' THEN(
    SELECT
        "rr"."customerReference" AS "customer_reference"
    FROM "disputes"."refunds" r
    JOIN "disputes"."refundReceipts" rr ON "rr"."refundId" = "r"."id"
    WHERE "r"."creditTransactionId" = "t"."id") ELSE "t"."transactionReference" END AS Reference,
    "t"."transactionAt" AS TransactionAt,
    "t"."description" AS "description",
    "t"."merchantName" AS MerchantName,
    "t"."amount" AS "amount",
    "t"."currencyCode" AS "currency",
    (
    SELECT
        "related3"."code"
    FROM "configuration"."transactionTypes" AS "related3"
    WHERE "related3"."id" = "t"."transactionTypeId") AS Type,
    "t"."direction" AS "direction",
    "t"."transactionStatus" AS Status,
    {{EligibleTransactionExpression}} AS DisputeEligible,
    {{ExistsAsBoolean}} AS HasActiveDispute,
    COALESCE((
        SELECT
            "d"."publicId" AS "id"
        FROM "disputes"."disputes" d
        WHERE "d"."transactionId" = "t"."id"
        ORDER BY CASE WHEN "d"."status" IN ('Submitted', 'UnderReview', 'AwaitingCustomer') THEN 0 ELSE 1 END, "d"."createdAt" DESC, "d"."id" DESC
        OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY), (
        SELECT
            (
            SELECT
                "related4"."publicId"
            FROM "disputes"."disputes" AS "related4"
            WHERE "related4"."id" = "r"."disputeId") AS "dispute_id"
        FROM "disputes"."refunds" r
        WHERE "r"."creditTransactionId" = "t"."id")) AS DisputeId,
    COALESCE((
        SELECT
            "d"."status" AS "status"
        FROM "disputes"."disputes" d
        WHERE "d"."transactionId" = "t"."id"
        ORDER BY CASE WHEN "d"."status" IN ('Submitted', 'UnderReview', 'AwaitingCustomer') THEN 0 ELSE 1 END, "d"."createdAt" DESC, "d"."id" DESC
        OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY), (
        SELECT
            "d"."status" AS "status"
        FROM "disputes"."refunds" r
        JOIN "disputes"."disputes" d ON "d"."id" = "r"."disputeId"
        WHERE "r"."creditTransactionId" = "t"."id")) AS DisputeStatus,
    COALESCE((
        SELECT
            "tt"."displayName" AS "display_name"
        FROM "configuration"."transactionTypes" tt
        WHERE "tt"."id" = "t"."transactionTypeId"), (
        SELECT
            "related5"."code"
        FROM "configuration"."transactionTypes" AS "related5"
        WHERE "related5"."id" = "t"."transactionTypeId")) AS TypeLabel
FROM "transactions"."transactions" t
JOIN "accounts"."accounts" a ON "a"."id" = "t"."accountId"
