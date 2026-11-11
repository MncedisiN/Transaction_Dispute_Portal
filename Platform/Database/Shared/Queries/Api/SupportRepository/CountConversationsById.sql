SELECT
    COUNT(*)
FROM "support"."conversations" st
LEFT JOIN "disputes"."disputes" d ON "d"."id" = "st"."disputeId"
WHERE "st"."publicId" = @Id
AND {{ThreadScope}}
