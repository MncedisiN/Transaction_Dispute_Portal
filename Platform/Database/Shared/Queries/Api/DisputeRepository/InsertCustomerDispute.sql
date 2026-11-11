INSERT INTO "disputes"."disputes"(
    "publicId",
    "reference",
    "transactionId",
    "customerId",
    "categoryId",
    "riskPolicyVersionId",
    "reason",
    "description",
    "status",
    "priority",
    "idempotencyKey",
    "submittedById",
    "specialistReview"
)
VALUES (
    @Id,
    @Reference,
    (
        SELECT
            "resolved1"."id"
        FROM "transactions"."transactions" AS "resolved1"
        WHERE "resolved1"."publicId" = @TransactionId),
    (
        SELECT
            "resolved2"."id"
        FROM "identity"."users" AS "resolved2"
        WHERE "resolved2"."publicId" = @CustomerId),
    @CategoryId,
    (
        SELECT
            id
        FROM configuration."riskPolicies"
        WHERE "publicId" = @PolicySnapshot),
    @Reason,
    @Description,
    'Submitted',
    2,
    @Key,
    (
        SELECT
            "resolved4"."id"
        FROM "identity"."users" AS "resolved4"
        WHERE "resolved4"."publicId" = @Actor),
    @SpecialistReview
)
