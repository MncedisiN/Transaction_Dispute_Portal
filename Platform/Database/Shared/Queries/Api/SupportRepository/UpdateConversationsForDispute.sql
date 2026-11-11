UPDATE "support"."conversations"
SET
    "disputeId" = (
    SELECT
        "resolved1"."id"
    FROM "disputes"."disputes" AS "resolved1"
    WHERE "resolved1"."publicId" = @Dispute)
WHERE "conversations"."publicId" = @Id
