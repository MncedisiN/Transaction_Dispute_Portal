{{ThreadSelect}}
WHERE "st"."conversationStatus" = 'Open'
AND(
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "st"."customerId") = @ActorId
AND "st"."relatedDisputeId" = (
    SELECT
        "rd"."id" AS "row_id"
    FROM "disputes"."disputes" rd
    WHERE "rd"."publicId" = @Related)
ORDER BY "st"."createdAt" DESC, "st"."id" DESC
OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY
