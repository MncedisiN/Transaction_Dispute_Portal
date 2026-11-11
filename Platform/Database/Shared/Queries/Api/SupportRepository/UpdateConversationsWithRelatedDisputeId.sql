UPDATE "support"."conversations"
SET
    "relatedDisputeId" = (
    SELECT
        "transaction_disputes"."id" AS "row_id"
    FROM "disputes"."disputes" AS "transaction_disputes"
    WHERE "transaction_disputes"."publicId" = @Related),
    "relatedCaseContext" = @Context
WHERE "conversations"."publicId" = @Id
