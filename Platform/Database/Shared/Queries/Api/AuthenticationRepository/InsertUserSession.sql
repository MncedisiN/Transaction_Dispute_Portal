INSERT INTO "identity"."userSessions"(
    "publicId",
    "userId",
    "expiresAt"
)
VALUES (
    @Id,
    (
        SELECT
            "resolved1"."id"
        FROM "identity"."users" AS "resolved1"
        WHERE "resolved1"."publicId" = @UserId),
    @ExpiresAt
)
