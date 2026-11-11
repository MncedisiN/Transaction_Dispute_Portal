{{ThreadSelect}}
WHERE "st"."conversationStatus" = 'Open'
AND "st"."disputeId" IS NULL
AND "st"."assignedToId" IS NULL
AND "st"."escalationLevel" = 0
ORDER BY "st"."priority" DESC, "st"."createdAt", "st"."publicId"
OFFSET 0 ROWS FETCH NEXT 100 ROWS ONLY
