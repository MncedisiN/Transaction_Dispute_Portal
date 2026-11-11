INSERT INTO "support"."staffCapacity"(
    "userId",
    "availability"
)
SELECT
    "u"."id",
    @Availability
FROM "identity"."users" AS "u"
WHERE "u"."publicId" = @Id
AND NOT EXISTS (
    SELECT
        1
    FROM "support"."staffCapacity" AS "capacity"
    WHERE "capacity"."userId" = "u"."id");

UPDATE "support"."staffCapacity"
SET
    "availability" = @Availability
WHERE "userId" = (
    SELECT
        "id"
    FROM "identity"."users"
    WHERE "publicId" = @Id)
