INSERT INTO "notifications"."deliveries"(
    "publicId",
    "userId",
    "disputeId",
    "milestone",
    "channel",
    "nextAttemptAt",
    "createdAt",
    "workflowCycle"
)
SELECT
    @Id,
    (
    SELECT
        "resolved3"."id"
    FROM "identity"."users" AS "resolved3"
    WHERE "resolved3"."publicId" = @User),
    (
    SELECT
        "resolved4"."id"
    FROM "disputes"."disputes" AS "resolved4"
    WHERE "resolved4"."publicId" = @Dispute),
    @Milestone,
    @Channel,
    @Now,
    @Now,
    @Cycle
WHERE NOT EXISTS (
    SELECT
        1
    FROM "notifications"."deliveries" AS "notification_deliveries"
    WHERE (
        SELECT
            "related1"."publicId"
        FROM "identity"."users" AS "related1"
        WHERE "related1"."id" = "notification_deliveries"."userId") = @User
    AND(
        SELECT
            "related2"."publicId"
        FROM "disputes"."disputes" AS "related2"
        WHERE "related2"."id" = "notification_deliveries"."disputeId") = @Dispute
    AND "notification_deliveries"."workflowCycle" = @Cycle
    AND "notification_deliveries"."milestone" = @Milestone
    AND "notification_deliveries"."channel" = @Channel)
