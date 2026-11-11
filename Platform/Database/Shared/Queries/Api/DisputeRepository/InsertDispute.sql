INSERT INTO "disputes"."disputes"(
    "publicId",
    "reference",
    "transactionId",
    "customerId",
    "categoryId",
    "reason",
    "description",
    "status",
    "priority",
    "idempotencyKey",
    "submittedById",
    "specialistReview"
)
SELECT
    @Child,
    @Reference,
    "transaction_disputes"."transactionId",
    "transaction_disputes"."customerId",
    "transaction_disputes"."categoryId",
    @Reason,
    "transaction_disputes"."description",
    'Submitted',
    "transaction_disputes"."priority",
    @Key,
    (
    SELECT
        "resolved4"."id"
    FROM "identity"."users" AS "resolved4"
    WHERE "resolved4"."publicId" = @Customer),
    {{TrueValue}}
FROM "disputes"."disputes" AS "transaction_disputes"
WHERE "transaction_disputes"."publicId" = @Parent
