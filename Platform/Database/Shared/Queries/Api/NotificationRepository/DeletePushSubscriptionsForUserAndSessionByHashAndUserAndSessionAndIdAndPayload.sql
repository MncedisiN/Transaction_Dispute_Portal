DELETE
FROM "notifications"."pushSubscriptions"
WHERE "pushSubscriptions"."endpointHash" = @Hash
OR((
        SELECT
            "related1"."publicId"
        FROM "identity"."users" AS "related1"
        WHERE "related1"."id" = "pushSubscriptions"."userId") = @User
    AND(
        SELECT
            "related2"."publicId"
        FROM "identity"."userSessions" AS "related2"
        WHERE "related2"."id" = "pushSubscriptions"."sessionId") = @Session);

INSERT INTO "notifications"."pushSubscriptions"(
    "publicId",
    "userId",
    "sessionId",
    "endpointHash",
    "protectedSubscription",
    "createdAt"
)
VALUES (
    @Id,
    (
        SELECT
            "resolved3"."id"
        FROM "identity"."users" AS "resolved3"
        WHERE "resolved3"."publicId" = @User),
    (
        SELECT
            "resolved4"."id"
        FROM "identity"."userSessions" AS "resolved4"
        WHERE "resolved4"."publicId" = @Session),
    @Hash,
    @Payload,
    @Now
)
