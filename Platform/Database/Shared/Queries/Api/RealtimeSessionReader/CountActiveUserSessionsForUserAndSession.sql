SELECT
    COUNT(*)
FROM "identity"."userSessions" s
JOIN "identity"."users" u ON "u"."id" = "s"."userId"
WHERE "s"."publicId" = @Session
AND(
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "s"."userId") = @User
AND "s"."revokedAt" IS NULL
AND "s"."expiresAt" > @Now
AND "s"."lastActivityAt" > @IdleCutoff
AND "s"."createdAt" > @AbsoluteCutoff
AND "u"."accountStatus" = 'Active'
