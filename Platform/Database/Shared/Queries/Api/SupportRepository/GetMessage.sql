SELECT
    "m"."sequenceNo" AS Sequence,
    "reader"."id" AS UserId,
    "st"."id" AS ConversationId
FROM "support"."messages" m
JOIN "support"."conversations" st ON "st"."id" = "m"."threadId"
JOIN "identity"."users" reader ON "reader"."publicId" = @ActorId
LEFT JOIN "disputes"."disputes" d ON "d"."id" = "st"."disputeId"
WHERE "m"."publicId" = @Message
AND "st"."publicId" = @Thread
AND {{ThreadScope}}
