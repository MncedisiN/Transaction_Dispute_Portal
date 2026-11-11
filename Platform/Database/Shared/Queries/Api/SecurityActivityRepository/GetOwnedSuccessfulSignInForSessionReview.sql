SELECT
    "e"."publicId" AS "id",
    "e"."eventType" AS EventType,
    "e"."outcome" AS "outcome",
    "e"."ipAddress" AS IpAddress,
    "e"."deviceType" AS DeviceType,
    "e"."browser" AS "browser",
    "e"."userAgent" AS UserAgent,
    "e"."location" AS "location",
    "e"."occurredAt" AS OccurredAt,
    "e"."observedSessionPublicId" AS SessionId,
    {{ExistsAsBoolean}} AS SessionActive
FROM "identity"."securityEvents" e
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "e"."userId") = @User
AND "e"."publicId" = @Id
AND "e"."eventType" = 'SignIn'
AND "e"."outcome" = 'Success'
