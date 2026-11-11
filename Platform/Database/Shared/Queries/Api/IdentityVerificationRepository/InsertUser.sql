INSERT INTO "identity"."users"(
    "publicId",
    "loginIdentifier",
    "normalizedLoginIdentifier",
    "passwordHash"
)
VALUES (
    @Id,
    @Contact,
    @Normalized,
    @PasswordHash
);

INSERT INTO "identity"."customerProfiles"(
    "userId",
    "displayName",
    "firstName",
    "lastName",
    "maskedEmail",
    "maskedPhone",
    "email",
    "phone",
    "emailVerified",
    "phoneVerified",
    "preferredOtpChannel"
)
VALUES (
    (
        SELECT
            "resolved1"."id"
        FROM "identity"."users" AS "resolved1"
        WHERE "resolved1"."publicId" = @Id),
    @Name,
    @FirstName,
    @LastName,
    @MaskedEmail,
    @MaskedPhone,
    @Email,
    @Phone,
    @EmailVerified,
    @PhoneVerified,
    @Channel
);

INSERT INTO "identity"."userRoles"(
    "userId",
    "roleId"
)
SELECT
    (
    SELECT
        "resolved1"."id"
    FROM "identity"."users" AS "resolved1"
    WHERE "resolved1"."publicId" = @Id),
    "roles"."id"
FROM "identity"."roles" AS "roles"
WHERE "roles"."code" = 'customer'
