INSERT INTO "identity"."users"(
    "publicId",
    "loginIdentifier",
    "normalizedLoginIdentifier",
    "passwordHash"
)
VALUES (
    @Id,
    @Email,
    @Normalized,
    @Hash
);

INSERT INTO "identity"."customerProfiles"(
    "userId",
    "displayName",
    "firstName",
    "middleName",
    "lastName",
    "maskedEmail",
    "maskedPhone"
)
VALUES (
    (
        SELECT
            "resolved1"."id"
        FROM "identity"."users" AS "resolved1"
        WHERE "resolved1"."publicId" = @Id),
    @Name,
    @FirstName,
    @MiddleName,
    @LastName,
    @Masked,
    'Not supplied'
);

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
    "userId"
)
VALUES (
    (
        SELECT
            "resolved3"."id"
        FROM "identity"."users" AS "resolved3"
        WHERE "resolved3"."publicId" = @Id)
);
