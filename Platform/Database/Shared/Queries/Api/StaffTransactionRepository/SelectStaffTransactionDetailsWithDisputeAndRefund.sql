SELECT
    "t"."publicId" AS "id",
    "u"."publicId" AS CustomerId,
    "p"."displayName" AS CustomerName,
    COALESCE("p"."email", CASE WHEN "u"."loginIdentifier" LIKE '%@%' THEN "u"."loginIdentifier" END) AS Email,
    "a"."publicId" AS AccountId,
    "a"."accountType" AS AccountType,
    "a"."maskedAccountNumber" AS AccountNumber,
    "t"."transactionReference" AS Reference,
    "t"."transactionAt" AS TransactionAt,
    "t"."description" AS "description",
    "t"."merchantName" AS MerchantName,
    "t"."amount" AS "amount",
    "t"."currencyCode" AS "currency",
    (
    SELECT
        "related1"."code"
    FROM "configuration"."transactionTypes" AS "related1"
    WHERE "related1"."id" = "t"."transactionTypeId") AS Type,
    "t"."direction" AS "direction",
    "t"."transactionStatus" AS Status,
    {{EligibleTransactionExpression}} AS DisputeEligible,
    COALESCE("d"."publicId", (
        SELECT
            (
            SELECT
                "related2"."publicId"
            FROM "disputes"."disputes" AS "related2"
            WHERE "related2"."id" = "r"."disputeId") AS "dispute_id"
        FROM "disputes"."refunds" r
        WHERE "r"."creditTransactionId" = "t"."id")) AS DisputeId,
    (
    SELECT
        "rr"."customerReference" AS "customer_reference"
    FROM "disputes"."refunds" r
    JOIN "disputes"."refundReceipts" rr ON "rr"."refundId" = "r"."id"
    WHERE "r"."creditTransactionId" = "t"."id") AS RefundReference
