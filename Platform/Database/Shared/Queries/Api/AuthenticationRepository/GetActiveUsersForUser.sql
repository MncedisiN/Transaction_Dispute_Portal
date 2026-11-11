SELECT
    "u"."publicId" AS Id,
    "p"."displayName" AS DisplayName,
    "p"."maskedEmail" AS MaskedEmail,
    COALESCE((
        SELECT
            string_agg("r"."code", '|')
        FROM "identity"."userRoles" ur
        JOIN "identity"."roles" r ON "r"."id" = "ur"."roleId"
        WHERE "ur"."userId" = "u"."id"), '') AS Roles,
    COALESCE((
        SELECT
            string_agg(permissions.permission_key, '|')
        FROM(
            SELECT
                DISTINCT "pm"."code" AS "permission_key"
            FROM "identity"."userRoles" ur
            JOIN "identity"."rolePermissions" rp ON "rp"."roleId" = "ur"."roleId"
            JOIN "identity"."permissions" pm ON "pm"."id" = "rp"."permissionId"
            WHERE "ur"."userId" = "u"."id") permissions), '') AS Permissions
FROM "identity"."users" u
JOIN "identity"."customerProfiles" p ON "p"."userId" = "u"."id"
WHERE "u"."publicId" = @UserId
AND "u"."accountStatus" = 'Active'
