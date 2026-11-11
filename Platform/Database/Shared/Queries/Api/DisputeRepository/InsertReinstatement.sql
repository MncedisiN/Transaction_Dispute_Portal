INSERT INTO "disputes"."reinstatements"(
    "parentDisputeId",
    "childDisputeId",
    "customerId",
    "requestId",
    "expectedVersion",
    "reason",
    "createdAt"
)
SELECT
    "parent"."id",
    "child"."id",
    "parent"."customerId",
    @RequestId,
    @ExpectedVersion,
    @Reason,
    @Now
FROM "disputes"."disputes" parent
JOIN "disputes"."disputes" child ON "child"."publicId" = @Child
WHERE "parent"."publicId" = @Parent
