DELETE
FROM "notifications"."pushSubscriptions"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "pushSubscriptions"."userId") = @User
AND(
    SELECT
        "related2"."publicId"
    FROM "identity"."userSessions" AS "related2"
    WHERE "related2"."id" = "pushSubscriptions"."sessionId") = @Session
