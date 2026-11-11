SELECT
    COUNT(*)
FROM "disputes"."refundRecalls" r
JOIN "disputes"."disputes" d ON "d"."id" = "r"."disputeId"
WHERE "d"."publicId" = @Id
