SELECT
    "trusted_login_devices"."publicId" AS "id",
    "trusted_login_devices"."tokenHash" AS TokenHash,
    "trusted_login_devices"."passwordStamp" AS PasswordStamp,
    "trusted_login_devices"."agentHash" AS AgentHash,
    "trusted_login_devices"."deviceType" AS DeviceType,
    "trusted_login_devices"."browser" AS "browser",
    "trusted_login_devices"."createdAt" AS CreatedAt,
    "trusted_login_devices"."expiresAt" AS ExpiresAt,
    "trusted_login_devices"."lastUsedAt" AS LastUsedAt
FROM "identity"."trustedLoginDevices" AS "trusted_login_devices"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "trusted_login_devices"."userId") = @User
AND "trusted_login_devices"."revokedAt" IS NULL
AND "trusted_login_devices"."expiresAt" > @Now
ORDER BY "trusted_login_devices"."lastUsedAt" DESC, "trusted_login_devices"."publicId" DESC
