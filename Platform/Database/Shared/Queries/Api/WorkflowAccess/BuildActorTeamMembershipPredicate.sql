EXISTS (
    SELECT
        1
    FROM "identity"."userRoles" team_membership
    JOIN "identity".users team_actor ON team_actor.id = team_membership."userId"
    JOIN "identity".roles team_role ON team_role.id = team_membership."roleId"
    WHERE team_actor."publicId" = {{Actor}}
    AND({{Responsibilities}}))
