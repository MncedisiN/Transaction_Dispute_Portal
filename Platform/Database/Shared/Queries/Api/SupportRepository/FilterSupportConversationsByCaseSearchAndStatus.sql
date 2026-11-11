WHERE (@DisputeId IS NULL
    OR(
        SELECT
            "related1"."publicId"
        FROM "disputes"."disputes" AS "related1"
        WHERE "related1"."id" = "st"."disputeId") = @DisputeId)
AND(@RelatedDisputeId IS NULL
    OR "st"."relatedDisputeId" = (
        SELECT
            "rd"."id" AS "row_id"
        FROM "disputes"."disputes" rd
        WHERE "rd"."publicId" = @RelatedDisputeId))
AND(@TransactionId IS NULL
    OR COALESCE((
            SELECT
                "related2"."publicId"
            FROM "transactions"."transactions" AS "related2"
            WHERE "related2"."id" = "st"."transactionId"), (
            SELECT
                "related3"."publicId"
            FROM "transactions"."transactions" AS "related3"
            WHERE "related3"."id" = "d"."transactionId")) = @TransactionId)
AND {{ThreadScope}}
AND {{SearchClause}}
AND((@Status IS NULL)
    OR("d"."status" = @Status
        AND "st"."conversationStatus" = 'Open')
    OR(@Status = 'active'
        AND "st"."conversationStatus" = 'Open')
    OR(@Status = 'closed'
        AND "st"."conversationStatus" = 'Closed'))
AND(@From IS NULL
    OR COALESCE((
            SELECT
                MAX("sm"."createdAt")
            FROM "support"."messages" sm
            WHERE "sm"."threadId" = "st"."id"), "st"."createdAt") >= @From)
AND(@To IS NULL
    OR COALESCE((
            SELECT
                MAX("sm"."createdAt")
            FROM "support"."messages" sm
            WHERE "sm"."threadId" = "st"."id"), "st"."createdAt") < @To)
AND(@UnreadOnly = 0
    OR EXISTS (
        SELECT
            1
        FROM "support"."messages" um
        WHERE "um"."threadId" = "st"."id"
        AND(
            SELECT
                "related1"."publicId"
            FROM "identity"."users" AS "related1"
            WHERE "related1"."id" = "um"."authorId") <> @ActorId
        AND "um"."sequenceNo" > COALESCE((
                SELECT
                    ur."lastReadSequence"
                FROM support."readStates" ur
                WHERE ur."conversationId" = "st"."id"
                AND ur."userId" = (
                    SELECT
                        "users"."id" AS "row_id"
                    FROM "identity"."users" AS "users"
                    WHERE "users"."publicId" = @ActorId)), 0)))
AND(@AssignedOnly = 0
    OR "st"."assignedToId" = (
        SELECT
            id
        FROM "identity".users
        WHERE "publicId" = @ActorId))
AND(@OwnOnly = 0
    OR "st"."customerId" = (
        SELECT
            id
        FROM "identity".users
        WHERE "publicId" = @ActorId))
