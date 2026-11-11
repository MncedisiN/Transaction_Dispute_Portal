UPDATE "support"."conversations"
SET
    "escalationLevel" = "conversations"."escalationLevel" + 1,
    "assignedToId" = NULL,
    "assignedAt" = NULL,
    "version" = "conversations"."version" + 1
WHERE "conversations"."publicId" = @Id
AND "conversations"."version" = @Version
