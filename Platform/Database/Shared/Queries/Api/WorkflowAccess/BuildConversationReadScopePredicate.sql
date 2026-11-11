EXISTS (
    SELECT
        1
    FROM "identity".users access_user
    WHERE access_user."publicId" = {{Actor}}
    AND access_user."accountStatus" = 'Active')
AND(st."customerId" = (
        SELECT
            id
        FROM "identity".users
        WHERE "publicId" = {{RelatedActor}})
    OR({{PermissionPredicate}}
        AND({{Supervisor}}
            OR(st."escalationLevel" = 0
                AND(st."assignedToId" IS NULL
                    OR st."assignedToId" = (
                        SELECT
                            id
                        FROM "identity".users
                        WHERE "publicId" = {{AdditionalActor}}))
                AND {{Team}}))
        AND(st."disputeId" IS NULL
            OR EXISTS (
                SELECT
                    1
                FROM disputes.disputes d
                WHERE d.id = st."disputeId"
                AND {{CaseReadScope}}))
        AND(st."relatedDisputeId" IS NULL
            OR {{RelatedSupervisor}}
            OR EXISTS (
                SELECT
                    1
                FROM disputes.disputes linked_case
                WHERE linked_case.id = st."relatedDisputeId"
                AND linked_case."escalationLevel" = 0
                AND {{RelatedTeam}}))))
