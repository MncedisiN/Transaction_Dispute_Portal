({{PermissionPredicate}}
    AND EXISTS (
        SELECT
            1
        FROM "identity"."userRoles" supervisor_ur
        JOIN "identity".users supervisor_actor ON supervisor_actor.id = supervisor_ur."userId"
        JOIN "identity".roles supervisor_r ON supervisor_r.id = supervisor_ur."roleId"
        WHERE supervisor_actor."publicId" = {{Actor}}
        AND supervisor_r.code IN ('super_admin', 'operations_manager')))
