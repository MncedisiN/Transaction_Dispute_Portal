SELECT
    1
FROM "identity"."userSessions" s
WHERE s."publicId" = e."observedSessionPublicId"
AND s."userId" = (
    SELECT
        sessionOwner."id"
    FROM "identity"."users" sessionOwner
    WHERE sessionOwner."publicId" = @User)
AND s."revokedAt" IS NULL
AND s."expiresAt" > @Now
AND s."lastActivityAt" > @IdleCutoff
AND s."createdAt" > @AbsoluteCutoff
