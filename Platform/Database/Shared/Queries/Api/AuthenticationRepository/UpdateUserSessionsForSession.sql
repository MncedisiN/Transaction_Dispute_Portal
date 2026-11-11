UPDATE "identity"."userSessions"
SET
    "lastActivityAt" = @Now
WHERE "userSessions"."publicId" = @SessionId
AND "userSessions"."revokedAt" IS NULL
AND "userSessions"."expiresAt" > @Now
AND "userSessions"."lastActivityAt" > @IdleCutoff
AND "userSessions"."createdAt" > @AbsoluteCutoff
AND EXISTS (
    SELECT
        1
    FROM "identity"."users" u
    WHERE "u"."id" = "userSessions"."userId"
    AND "u"."accountStatus" = 'Active')
