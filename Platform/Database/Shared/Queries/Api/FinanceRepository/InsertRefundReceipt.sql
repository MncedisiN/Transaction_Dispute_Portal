INSERT INTO "disputes"."refundReceipts"(
    "refundId",
    "customerReference"
)
VALUES (
    (
        SELECT
            "resolved1"."id"
        FROM "disputes"."refunds" AS "resolved1"
        WHERE (
            SELECT
                "related2"."publicId"
            FROM "disputes"."disputes" AS "related2"
            WHERE "related2"."id" = "resolved1"."disputeId") = @Id),
    @Reference
)
