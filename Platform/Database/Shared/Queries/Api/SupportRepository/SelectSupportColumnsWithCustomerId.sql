(st."customerId" = (
        SELECT
            id
        FROM "identity".users
        WHERE "publicId" = @ActorId)
    OR EXISTS (
        SELECT
            1
        FROM disputes.disputes d
        WHERE d.id = st."relatedDisputeId"
        AND {{CaseReadScope}}))
