SELECT
    "st"."conversationStatus" AS Status,
    (
    SELECT
        "related1"."publicId"
    FROM "support"."conversations" AS "related1"
    WHERE "related1"."id" = "m"."threadId") AS ThreadId,
    "m"."body" AS "body",
    (
    SELECT
        "related2"."publicId"
    FROM "disputes"."disputes" AS "related2"
    WHERE "related2"."id" = "st"."disputeId") AS DisputeId,
    (
    SELECT
        "related3"."publicId"
    FROM "transactions"."transactions" AS "related3"
    WHERE "related3"."id" = "st"."transactionId") AS TransactionId,
    (
    SELECT
        "rd"."publicId" AS "id"
    FROM "disputes"."disputes" rd
    WHERE "rd"."id" = "st"."relatedDisputeId") AS RelatedDisputeId,
    "m"."requestSubject" AS Subject,
    "m"."enquiryRequestHash" AS EnquiryRequestHash,
    (
    SELECT
        "concern"."code" AS "code"
    FROM "support"."concerns" concern
    WHERE "concern"."id" = "st"."concernId") AS ConcernCode
FROM "support"."messages" m
JOIN "support"."conversations" st ON "st"."id" = "m"."threadId"
LEFT JOIN "disputes"."disputes" d ON "d"."id" = "st"."disputeId"
WHERE (
    SELECT
        "related4"."publicId"
    FROM "identity"."users" AS "related4"
    WHERE "related4"."id" = "m"."authorId") = @ActorId
AND "m"."clientMessageId" = @Request
AND {{ThreadScope}}
