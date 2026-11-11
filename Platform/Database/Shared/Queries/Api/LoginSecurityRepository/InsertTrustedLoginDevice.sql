INSERT INTO "identity"."trustedLoginDevices"(
    "publicId",
    "userId",
    "tokenHash",
    "passwordStamp",
    "agentHash",
    "deviceType",
    "browser",
    "createdAt",
    "expiresAt",
    "lastUsedAt"
)
VALUES (
    @Id,
    (
        SELECT
            "resolved1"."id"
        FROM "identity"."users" AS "resolved1"
        WHERE "resolved1"."publicId" = @User),
    @Hash,
    @Password,
    @Agent,
    @Device,
    @Browser,
    @Now,
    @Expires,
    @Now
)
