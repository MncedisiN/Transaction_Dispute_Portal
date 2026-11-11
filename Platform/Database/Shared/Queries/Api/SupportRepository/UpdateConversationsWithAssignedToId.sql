UPDATE "support"."conversations"
SET
    "assignedToId" = (
    SELECT
        "users"."id" AS "row_id"
    FROM "identity"."users" AS "users"
    WHERE "users"."publicId" = @Selected),
    "assignedAt" = @Now,
    "version" = "conversations"."version" + 1
WHERE "conversations"."publicId" = @Id
AND "conversations"."version" = @Version
AND "conversations"."conversationStatus" = 'Open'
