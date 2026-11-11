SELECT
    "e"."publicId" AS "id",
    'Evidence' AS Source,
    'EvidenceSubmitted' AS Action,
    "p"."displayName" AS Actor,
    "e"."createdAt" AS OccurredAt,
    "e"."category" AS Detail
FROM "disputes"."evidence" e
JOIN "identity"."customerProfiles" p ON "p"."userId" = "e"."uploadedById"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "disputes"."disputes" AS "related1"
    WHERE "related1"."id" = "e"."disputeId") = @Id
