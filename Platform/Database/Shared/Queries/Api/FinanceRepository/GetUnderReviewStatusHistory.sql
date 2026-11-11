SELECT
    "h"."publicId" AS "id",
    'History' AS Source,
    CASE WHEN "h"."previousStatus" IN ('Resolved', 'Declined', 'Withdrawn')
AND "h"."newStatus" = 'UnderReview' THEN 'Reopened' ELSE "h"."newStatus" END AS Action,
    COALESCE("p"."displayName", 'System') AS Actor,
    "h"."changedAt" AS OccurredAt,
    "h"."comment" AS Detail
FROM "disputes"."statusHistory" h
LEFT JOIN "identity"."customerProfiles" p ON "p"."userId" = "h"."actorUserId"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "disputes"."disputes" AS "related1"
    WHERE "related1"."id" = "h"."disputeId") = @Id
