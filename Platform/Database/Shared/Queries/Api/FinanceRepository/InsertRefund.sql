INSERT INTO "disputes"."refunds"(
    "disputeId",
    "transactionId",
    "creditTransactionId",
    "accountId",
    "customerId",
    "amount",
    "currencyCode",
    "approvedById",
    "requestId",
    "reason",
    "approvalSource",
    "policyVersion",
    "refundPolicyVersionId",
    "postedAt"
)
VALUES (
    (
        SELECT
            "resolved1"."id"
        FROM "disputes"."disputes" AS "resolved1"
        WHERE "resolved1"."publicId" = @Id),
    (
        SELECT
            "resolved2"."id"
        FROM "transactions"."transactions" AS "resolved2"
        WHERE "resolved2"."publicId" = @TransactionId),
    (
        SELECT
            "resolved3"."id"
        FROM "transactions"."transactions" AS "resolved3"
        WHERE "resolved3"."publicId" = @Credit),
    (
        SELECT
            "resolved4"."id"
        FROM "accounts"."accounts" AS "resolved4"
        WHERE "resolved4"."publicId" = @Account),
    (
        SELECT
            "resolved5"."id"
        FROM "identity"."users" AS "resolved5"
        WHERE "resolved5"."publicId" = @Customer),
    @Amount,
    @Currency,
    (
        SELECT
            "resolved6"."id"
        FROM "identity"."users" AS "resolved6"
        WHERE "resolved6"."publicId" = @Actor),
    @Request,
    @Reason,
    @Source,
    @Policy,
    COALESCE((
            SELECT
                command."refundPolicyVersionId"
            FROM disputes."financialCommands" command
            WHERE command."requestId" = @Request), (
            SELECT
                decision."refundPolicyVersionId"
            FROM disputes."automaticDecisions" decision
            JOIN disputes.disputes decision_case ON decision_case.id = decision."disputeId"
            WHERE decision_case."publicId" = @Id
            AND decision."policyVersion" = @Policy)),
    @Now
)
