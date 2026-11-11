UPDATE "identity"."userSessions"
SET
    "ipAddress" = @Ip,
    "deviceType" = @Device,
    "browser" = @Browser
WHERE "userSessions"."publicId" = @Id
AND(
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "userSessions"."userId") = @User
