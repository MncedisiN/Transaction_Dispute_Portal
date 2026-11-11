SELECT
    (
    SELECT
        "related1"."code"
    FROM "configuration"."disputeCategories" AS "related1"
    WHERE "related1"."id" = "transaction_disputes"."categoryId") AS "category"
FROM "disputes"."disputes" AS "transaction_disputes"
WHERE "transaction_disputes"."publicId" = @Id
