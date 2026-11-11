UPDATE "disputes"."disputes"
SET
    "priority" = @Priority,
    "severity" = @Severity,
    "queueType" = @QueueType,
    "responseDueAt" = @Response,
    "dueAt" = @Due,
    "assignedToId" = (
    SELECT
        "resolved1"."id"
    FROM "identity"."users" AS "resolved1"
    WHERE "resolved1"."publicId" = @Assigned),
    "assignedAt" = @AssignedAt,
    "version" = "disputes"."version" + 1,
    "updatedAt" = @Now
WHERE "disputes"."publicId" = @Id
