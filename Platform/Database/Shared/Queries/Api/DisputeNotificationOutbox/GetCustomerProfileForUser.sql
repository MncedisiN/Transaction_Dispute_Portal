SELECT
    COALESCE("n"."channel", "p"."preferredOtpChannel")
FROM "identity"."customerProfiles" p
LEFT JOIN "notifications"."preferences" n ON "n"."userId" = "p"."userId"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "p"."userId") = @User
