UPDATE "support"."conversations"
SET
    "assignedToId" = (
    SELECT
        "users"."id" AS "row_id"
    FROM "identity"."users" AS "users"
    WHERE "users"."publicId" = @Selected),
    "assignedAt" = @Now,
    "queueType" = @QueueType,
    "escalationLevel" = @EscalationLevel,
    "version" = "conversations"."version" + 1
WHERE (
    SELECT
        "related1"."publicId"
    FROM "disputes"."disputes" AS "related1"
    WHERE "related1"."id" = "conversations"."disputeId") = @Id
AND "conversations"."conversationStatus" = 'Open'
