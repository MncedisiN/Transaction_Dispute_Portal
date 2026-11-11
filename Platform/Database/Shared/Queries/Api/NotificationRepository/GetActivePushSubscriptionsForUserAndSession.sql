SELECT
    "p"."publicId" AS "id",
    "p"."protectedSubscription" AS Payload
FROM "notifications"."pushSubscriptions" p
JOIN "identity"."userSessions" s ON "s"."id" = "p"."sessionId"
JOIN "identity"."users" u ON "u"."id" = "p"."userId"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "p"."userId") = @User
AND(@Session IS NULL
    OR(
        SELECT
            "related2"."publicId"
        FROM "identity"."userSessions" AS "related2"
        WHERE "related2"."id" = "p"."sessionId") = @Session)
AND "s"."revokedAt" IS NULL
AND "u"."accountStatus" = 'Active'
