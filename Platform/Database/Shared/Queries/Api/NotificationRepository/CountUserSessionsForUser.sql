SELECT
    COUNT(*)
FROM "identity"."userSessions" AS "user_sessions"
WHERE "user_sessions"."publicId" = @Id
AND(
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "user_sessions"."userId") = @User
AND "user_sessions"."revokedAt" IS NULL
AND "user_sessions"."expiresAt" > @Now
