SELECT
    "m"."publicId" AS "id",
    'Message' AS Source,
    CASE WHEN "m"."authorId" = "t"."customerId" THEN 'CustomerMessage' ELSE 'AgentReply' END AS Action,
    "p"."displayName" AS Actor,
    "m"."createdAt" AS OccurredAt,
    NULL AS Detail
FROM "support"."messages" m
JOIN "support"."conversations" t ON "t"."id" = "m"."threadId"
JOIN "identity"."customerProfiles" p ON "p"."userId" = "m"."authorId"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "disputes"."disputes" AS "related1"
    WHERE "related1"."id" = "t"."disputeId") = @Id
ORDER BY "m"."createdAt" DESC
OFFSET 0 ROWS FETCH NEXT 200 ROWS ONLY
