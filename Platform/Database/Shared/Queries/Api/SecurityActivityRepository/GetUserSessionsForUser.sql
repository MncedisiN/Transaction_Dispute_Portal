SELECT
    "user_sessions"."publicId" AS "id",
    "user_sessions"."createdAt" AS CreatedAt,
    "user_sessions"."expiresAt" AS ExpiresAt,
    "user_sessions"."ipAddress" AS IpAddress,
    "user_sessions"."deviceType" AS DeviceType,
    "user_sessions"."browser" AS "browser"
FROM "identity"."userSessions" AS "user_sessions"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "user_sessions"."userId") = @User
AND "user_sessions"."revokedAt" IS NULL
AND "user_sessions"."expiresAt" > @Now
AND "user_sessions"."lastActivityAt" > @IdleCutoff
AND "user_sessions"."createdAt" > @AbsoluteCutoff
ORDER BY "user_sessions"."createdAt" DESC
