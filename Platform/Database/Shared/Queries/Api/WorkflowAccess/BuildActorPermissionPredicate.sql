EXISTS (
    SELECT
        1
    FROM "identity"."userRoles" access_ur
    JOIN "identity".users access_actor ON access_actor.id = access_ur."userId"
    JOIN "identity"."rolePermissions" access_rp ON access_rp."roleId" = access_ur."roleId"
    JOIN "identity".permissions access_p ON access_p.id = access_rp."permissionId"
    WHERE access_actor."publicId" = {{Actor}}
    AND access_p.code = '{{PermissionPredicate}}')
