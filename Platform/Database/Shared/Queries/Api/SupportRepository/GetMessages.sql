SELECT
    "m"."publicId" AS "id",
    "m"."sequenceNo" AS Sequence,
    (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "m"."authorId") AS AuthorId,
    "p"."displayName" AS AuthorName,
    "m"."body" AS "body",
    {{AuditMetadataSelectExpression}} AS EnquiryJson,
    "m"."createdAt" AS CreatedAt
FROM "support"."messages" m
JOIN "identity"."customerProfiles" p ON "p"."userId" = "m"."authorId"
WHERE (
    SELECT
        "related2"."publicId"
    FROM "support"."conversations" AS "related2"
    WHERE "related2"."id" = "m"."threadId") = @Id
AND(@After IS NULL
    OR "m"."sequenceNo" > @After)
AND(@Before IS NULL
    OR "m"."sequenceNo" < @Before)
ORDER BY "m"."sequenceNo" {{Order}}
OFFSET 0 ROWS FETCH NEXT @Take ROWS ONLY
