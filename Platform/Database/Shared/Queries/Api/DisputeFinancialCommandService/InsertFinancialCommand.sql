INSERT INTO "disputes"."financialCommands"(
    "publicId",
    "requestId",
    "disputeId",
    "originalTransactionId",
    "accountId",
    "customerId",
    "kind",
    "amount",
    "currencyCode",
    "actorId",
    "reason",
    "policyVersion",
    "refundPolicyVersionId",
    "expectedVersion",
    "correlation",
    "state"
)
VALUES (
    @TransactionId,
    @RequestId,
    (
        SELECT
            "resolved1"."id"
        FROM "disputes"."disputes" AS "resolved1"
        WHERE "resolved1"."publicId" = @DisputeId),
    (
        SELECT
            "resolved2"."id"
        FROM "transactions"."transactions" AS "resolved2"
        WHERE "resolved2"."publicId" = @OriginalTransactionId),
    (
        SELECT
            "resolved3"."id"
        FROM "accounts"."accounts" AS "resolved3"
        WHERE "resolved3"."publicId" = @AccountId),
    (
        SELECT
            "resolved4"."id"
        FROM "identity"."users" AS "resolved4"
        WHERE "resolved4"."publicId" = @UserId),
    @Kind,
    @Amount,
    @Currency,
    (
        SELECT
            "resolved5"."id"
        FROM "identity"."users" AS "resolved5"
        WHERE "resolved5"."publicId" = @ActorId),
    @Reason,
    @PolicyVersion,
    (
        SELECT
            decision."refundPolicyVersionId"
        FROM disputes."automaticDecisions" decision
        JOIN disputes.disputes decision_case ON decision_case.id = decision."disputeId"
        WHERE decision_case."publicId" = @DisputeId
        AND decision."policyVersion" = @PolicyVersion),
    @ExpectedVersion,
    @Correlation,
    'Queued'
)
