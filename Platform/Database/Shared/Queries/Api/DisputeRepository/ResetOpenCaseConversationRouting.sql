UPDATE "support"."conversations"
SET
    "queueType" = @Queue,
    "escalationLevel" = 0,
    "assignedToId" = NULL,
    "version" = "conversations"."version" + 1
WHERE (
    SELECT
        "related1"."publicId"
    FROM "disputes"."disputes" AS "related1"
    WHERE "related1"."id" = "conversations"."disputeId") = @Id
AND "conversations"."conversationStatus" = 'Open'
