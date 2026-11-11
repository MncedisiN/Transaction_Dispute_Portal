SELECT
    "st"."publicId" AS "id"
FROM support."readStates" sr
JOIN "support"."conversations" st ON "st"."id" = sr."conversationId"
LEFT JOIN "disputes"."disputes" d ON "d"."id" = "st"."disputeId"
WHERE sr."userId" = (
    SELECT
        "users"."id" AS "row_id"
    FROM "identity"."users" AS "users"
    WHERE "users"."publicId" = @ActorId)
AND {{ThreadScope}}
ORDER BY sr."openedAt" DESC, st."id" DESC
OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY
