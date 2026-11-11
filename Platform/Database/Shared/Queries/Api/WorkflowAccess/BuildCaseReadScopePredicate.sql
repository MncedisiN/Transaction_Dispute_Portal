({{PermissionPredicate}}
    AND EXISTS (
        SELECT
            1
        FROM "identity".users case_actor
        WHERE case_actor."publicId" = {{Actor}}
        AND case_actor."accountStatus" = 'Active')
    AND({{Supervisor}}
        OR(d."escalationLevel" = 0
            AND(d."assignedToId" IS NULL
                OR d."assignedToId" = (
                    SELECT
                        id
                    FROM "identity".users
                    WHERE "publicId" = {{RelatedActor}}))
            AND {{Team}})))
