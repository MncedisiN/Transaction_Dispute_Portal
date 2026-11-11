INSERT INTO "support"."conversations"(
    "publicId",
    "customerId",
    "disputeId",
    "transactionId",
    "subject",
    "concernId"
)
VALUES (
    @Id,
    (
        SELECT
            "resolved1"."id"
        FROM "identity"."users" AS "resolved1"
        WHERE "resolved1"."publicId" = @CustomerId),
    (
        SELECT
            "resolved2"."id"
        FROM "disputes"."disputes" AS "resolved2"
        WHERE "resolved2"."publicId" = @DisputeId),
    (
        SELECT
            "resolved3"."id"
        FROM "transactions"."transactions" AS "resolved3"
        WHERE "resolved3"."publicId" = @TransactionId),
    @Subject,
    @ConcernId
)
