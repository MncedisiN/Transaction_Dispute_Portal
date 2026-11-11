UPDATE "identity"."userSessions"
SET
    "revokedAt" = @Now
WHERE "userSessions"."publicId" = @Id
AND(
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "userSessions"."userId") = @User
AND "userSessions"."revokedAt" IS NULL
