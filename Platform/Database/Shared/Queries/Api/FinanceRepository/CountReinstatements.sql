SELECT
    COUNT(*)
FROM "disputes"."reinstatements" r
JOIN "disputes"."disputes" d ON "d"."id" = "r"."parentDisputeId"
WHERE "d"."publicId" = @Id
