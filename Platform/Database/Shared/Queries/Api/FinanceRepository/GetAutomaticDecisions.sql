SELECT
    "d"."publicId" AS "id",
    'Automatic' AS Source,
    'AutomaticDecision' AS Action,
    'System' AS Actor,
    "d"."evaluatedAt" AS OccurredAt,
    "d"."reason" AS Detail
FROM "disputes"."automaticDecisions" d
WHERE (
    SELECT
        "related1"."publicId"
    FROM "disputes"."disputes" AS "related1"
    WHERE "related1"."id" = "d"."disputeId") = @Id
