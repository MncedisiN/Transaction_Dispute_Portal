UPDATE "disputes"."disputes"
SET
    "assignedToId" = (
    SELECT
        "resolved1"."id"
    FROM "identity"."users" AS "resolved1"
    WHERE "resolved1"."publicId" = @Selected),
    "assignedAt" = CASE WHEN @Selected IS NULL THEN NULL ELSE @Now END,
    "autoAssignmentPaused" = @Paused,
    "updatedAt" = @Now,
    "version" = "disputes"."version" + 1
WHERE "disputes"."publicId" = @Id;

UPDATE "support"."staffCapacity"
SET
    "lastAssignedAt" = @Now
WHERE (
    SELECT
        "related2"."publicId"
    FROM "identity"."users" AS "related2"
    WHERE "related2"."id" = "staffCapacity"."userId") = @Selected
