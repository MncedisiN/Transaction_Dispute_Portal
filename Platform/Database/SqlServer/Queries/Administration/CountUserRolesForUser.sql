SELECT
    COUNT(*)
FROM "identity"."userRoles" ur
JOIN "identity"."roles" r ON r.id = ur."roleId"
WHERE ur."userId" = @UserId
AND r.code = 'super_admin'
