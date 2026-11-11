{{ThreadSelect}}
WHERE "st"."conversationStatus" = 'Open'
AND(
    SELECT
        "related2"."publicId"
    FROM "identity"."users" AS "related2"
    WHERE "related2"."id" = "st"."customerId") = @Owner
AND "st"."relatedDisputeId" IS NULL
AND((@Dispute IS NOT NULL
        AND(
            SELECT
                "related3"."publicId"
            FROM "disputes"."disputes" AS "related3"
            WHERE "related3"."id" = "st"."disputeId") = @Dispute)
    OR((
            SELECT
                "related4"."publicId"
            FROM "disputes"."disputes" AS "related4"
            WHERE "related4"."id" = "st"."disputeId") IS NULL
        AND(
            SELECT
                "related5"."publicId"
            FROM "transactions"."transactions" AS "related5"
            WHERE "related5"."id" = "st"."transactionId") = @Transaction))
ORDER BY CASE WHEN(
    SELECT
        "related1"."publicId"
    FROM "disputes"."disputes" AS "related1"
    WHERE "related1"."id" = "st"."disputeId") = @Dispute THEN 0 ELSE 1 END, "st"."createdAt" DESC, "st"."id" DESC
OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY
