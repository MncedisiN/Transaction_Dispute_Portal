INSERT INTO "identity".users(
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

SELECT
    id
FROM "identity".users
WHERE "publicId" = @Id;
