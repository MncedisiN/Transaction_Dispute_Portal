SELECT
    "n"."publicId" AS "id",
    'Note' AS Source,
    'CustomerUpdate' AS Action,
    COALESCE("p"."displayName", 'System') AS Actor,
    "n"."createdAt" AS OccurredAt,
    "n"."content" AS Detail
FROM "disputes"."notes" n
LEFT JOIN "identity"."customerProfiles" p ON "p"."userId" = "n"."authorUserId"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "disputes"."disputes" AS "related1"
    WHERE "related1"."id" = "n"."disputeId") = @Id
AND "n"."visibility" = 'Customer'
AND "n"."content" <> 'Supporting evidence attached.'
AND NOT EXISTS (
    SELECT
        1
    FROM "support"."messages" m
    JOIN "support"."conversations" t ON "t"."id" = "m"."threadId"
    WHERE "t"."disputeId" = "n"."disputeId"
    AND "m"."authorId" = "n"."authorUserId"
    AND "m"."body" = "n"."content")
