SELECT
    u."publicId"
FROM "identity"."users" u
WHERE u."normalizedLoginIdentifier" = @Login
AND u."accountStatus" = 'Active'
AND EXISTS (
    SELECT
        1
    FROM "identity"."userRoles" ur
    JOIN "identity"."roles" r ON r.id = ur."roleId"
    WHERE ur."userId" = u.id
    AND r.code = 'super_admin')
