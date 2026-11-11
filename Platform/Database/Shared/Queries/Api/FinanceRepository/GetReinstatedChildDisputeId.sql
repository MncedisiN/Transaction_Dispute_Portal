SELECT
    "child"."publicId" AS "id"
FROM "disputes"."reinstatements" r
JOIN "disputes"."disputes" p ON "p"."id" = "r"."parentDisputeId"
JOIN "disputes"."disputes" child ON "child"."id" = "r"."childDisputeId"
WHERE "p"."publicId" = @Id
