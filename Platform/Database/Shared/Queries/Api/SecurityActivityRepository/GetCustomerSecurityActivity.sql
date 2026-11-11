SELECT
    "security_events"."publicId" AS "id",
    (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "security_events"."userId") AS UserId,
    "security_events"."eventType" AS EventType,
    "security_events"."outcome" AS "outcome",
    "security_events"."ipAddress" AS IpAddress,
    "security_events"."deviceType" AS DeviceType,
    "security_events"."browser" AS "browser",
    "security_events"."location" AS "location",
    "security_events"."occurredAt" AS OccurredAt
FROM "identity"."securityEvents" AS "security_events"
WHERE (@User IS NULL
    OR(
        SELECT
            "related2"."publicId"
        FROM "identity"."users" AS "related2"
        WHERE "related2"."id" = "security_events"."userId") = @User)
ORDER BY "security_events"."occurredAt" DESC, "security_events"."publicId" {{PaginationClause}}
