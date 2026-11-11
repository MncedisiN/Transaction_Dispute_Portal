SELECT
    "transaction_disputes"."publicId" AS "id",
    "transaction_disputes"."reference" AS "reference",
    "transaction_disputes"."status" AS "status",
    (
    SELECT
        "related1"."publicId"
    FROM "transactions"."transactions" AS "related1"
    WHERE "related1"."id" = "transaction_disputes"."transactionId") AS TransactionId,
    (
    SELECT
        "related2"."code"
    FROM "configuration"."disputeCategories" AS "related2"
    WHERE "related2"."id" = "transaction_disputes"."categoryId") AS "category",
    "transaction_disputes"."reason" AS "reason",
    "transaction_disputes"."description" AS "description",
    (
    SELECT
        "related3"."publicId"
    FROM "identity"."users" AS "related3"
    WHERE "related3"."id" = "transaction_disputes"."submittedById") AS SubmittedBy,
    "transaction_disputes"."specialistReview" AS SpecialistReview
FROM "disputes"."disputes" AS "transaction_disputes"
WHERE (
    SELECT
        "related4"."publicId"
    FROM "identity"."users" AS "related4"
    WHERE "related4"."id" = "transaction_disputes"."customerId") = @CustomerId
AND "transaction_disputes"."idempotencyKey" = @Key
