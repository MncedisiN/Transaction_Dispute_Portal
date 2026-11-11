UPDATE "support"."conversations"
SET
    "disputeId" = (
    SELECT
        "resolved1"."id"
    FROM "disputes"."disputes" AS "resolved1"
    WHERE "resolved1"."publicId" = @DisputeId)
WHERE (
    SELECT
        "related2"."publicId"
    FROM "transactions"."transactions" AS "related2"
    WHERE "related2"."id" = "conversations"."transactionId") = @TransactionId
AND(
    SELECT
        "related3"."publicId"
    FROM "identity"."users" AS "related3"
    WHERE "related3"."id" = "conversations"."customerId") = @CustomerId
AND(
    SELECT
        "related4"."publicId"
    FROM "disputes"."disputes" AS "related4"
    WHERE "related4"."id" = "conversations"."disputeId") IS NULL
AND "conversations"."conversationStatus" = 'Open'
