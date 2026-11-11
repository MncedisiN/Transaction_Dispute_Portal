UPDATE "identity"."users"
SET
    "accountStatus" = @Status,
    "updatedAt" = @Now
WHERE "users"."publicId" = @Id;

DELETE
FROM "identity"."userRoles"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "userRoles"."userId") = @Id;

INSERT INTO "identity"."userRoles"(
    "userId",
    "roleId"
)
SELECT
    (
    SELECT
        "resolved2"."id"
    FROM "identity"."users" AS "resolved2"
    WHERE "resolved2"."publicId" = @Id),
    "roles"."id"
FROM "identity"."roles" AS "roles"
WHERE "roles"."code" = @Role;

INSERT INTO "support"."staffCapacity"(
    "userId",
    "capacity"
)
SELECT
    "u"."id",
    @Capacity
FROM "identity"."users" AS "u"
WHERE "u"."publicId" = @Id
AND NOT EXISTS (
    SELECT
        1
    FROM "support"."staffCapacity" AS "capacity"
    WHERE "capacity"."userId" = "u"."id");

UPDATE "support"."staffCapacity"
SET
    "capacity" = @Capacity
WHERE "userId" = (
    SELECT
        "id"
    FROM "identity"."users"
    WHERE "publicId" = @Id);

UPDATE "identity"."userSessions"
SET
    "revokedAt" = @Now
WHERE (
    SELECT
        "related4"."publicId"
    FROM "identity"."users" AS "related4"
    WHERE "related4"."id" = "userSessions"."userId") = @Id
AND "userSessions"."revokedAt" IS NULL;
