SELECT
    DISTINCT permission.code
FROM "identity"."permissions" permission
JOIN "identity"."rolePermissions" grant_rule ON grant_rule."permissionId" = permission.id
JOIN "identity"."userRoles" membership ON membership."roleId" = grant_rule."roleId"
JOIN "identity".users actor_user ON actor_user.id = membership."userId"
WHERE actor_user."publicId" = @Actor
AND actor_user."accountStatus" = 'Active'
