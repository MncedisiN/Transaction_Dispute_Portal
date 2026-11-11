UPDATE "support"."conversations"
SET
    "conversationStatus" = 'Closed',
    "closedAt" = @Now,
    "closedById" = (
    SELECT
        "users"."id" AS "row_id"
    FROM "identity"."users" AS "users"
    WHERE "users"."publicId" = @Actor),
    "closureReason" = 'The financial case completed. Start a new enquiry for further assistance.',
    "version" = "conversations"."version" + 1
WHERE "conversations"."publicId" = @Thread
AND "conversations"."conversationStatus" = 'Open'
