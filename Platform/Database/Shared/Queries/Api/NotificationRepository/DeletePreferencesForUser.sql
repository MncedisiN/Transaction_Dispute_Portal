DELETE
FROM "notifications"."preferences"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "preferences"."userId") = @User;

INSERT INTO "notifications"."preferences"(
    "userId",
    "channel"
)
VALUES (
    (
        SELECT
            "resolved2"."id"
        FROM "identity"."users" AS "resolved2"
        WHERE "resolved2"."publicId" = @User),
    @Channel
)
