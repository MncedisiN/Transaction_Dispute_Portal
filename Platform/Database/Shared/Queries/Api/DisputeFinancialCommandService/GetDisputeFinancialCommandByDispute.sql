SELECT
    "dispute_financial_commands"."publicId" AS TransactionId,
    "dispute_financial_commands"."requestId" AS RequestId,
    (
    SELECT
        "related1"."publicId"
    FROM "disputes"."disputes" AS "related1"
    WHERE "related1"."id" = "dispute_financial_commands"."disputeId") AS DisputeId,
    (
    SELECT
        "related2"."publicId"
    FROM "transactions"."transactions" AS "related2"
    WHERE "related2"."id" = "dispute_financial_commands"."originalTransactionId") AS OriginalTransactionId,
    (
    SELECT
        "related3"."publicId"
    FROM "accounts"."accounts" AS "related3"
    WHERE "related3"."id" = "dispute_financial_commands"."accountId") AS AccountId,
    (
    SELECT
        "related4"."publicId"
    FROM "identity"."users" AS "related4"
    WHERE "related4"."id" = "dispute_financial_commands"."customerId") AS UserId,
    "dispute_financial_commands"."kind" AS "kind",
    "dispute_financial_commands"."amount" AS "amount",
    "dispute_financial_commands"."currencyCode" AS "currency",
    (
    SELECT
        "related5"."publicId"
    FROM "identity"."users" AS "related5"
    WHERE "related5"."id" = "dispute_financial_commands"."actorId") AS ActorId,
    "dispute_financial_commands"."reason" AS "reason",
    "dispute_financial_commands"."policyVersion" AS PolicyVersion,
    "dispute_financial_commands"."expectedVersion" AS ExpectedVersion,
    "dispute_financial_commands"."correlation" AS "correlation",
    "dispute_financial_commands"."state" AS "state",
    "dispute_financial_commands"."failureCode" AS FailureCode
FROM "disputes"."financialCommands" AS "dispute_financial_commands"
WHERE "dispute_financial_commands"."disputeId" = (
    SELECT
        "pendingCase"."id"
    FROM "disputes"."disputes" AS "pendingCase"
    WHERE "pendingCase"."publicId" = @Dispute)
ORDER BY "dispute_financial_commands"."createdAt" DESC
OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY
