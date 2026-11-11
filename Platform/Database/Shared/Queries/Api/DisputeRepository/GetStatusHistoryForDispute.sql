SELECT
    "dispute_status_history"."publicId" AS "id",
    "dispute_status_history"."previousStatus" AS PreviousStatus,
    "dispute_status_history"."newStatus" AS NewStatus,
    "dispute_status_history"."changedAt" AS ChangedAt,
    "dispute_status_history"."comment" AS "comment"
FROM "disputes"."statusHistory" AS "dispute_status_history"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "disputes"."disputes" AS "related1"
    WHERE "related1"."id" = "dispute_status_history"."disputeId") = @DisputeId
ORDER BY "dispute_status_history"."changedAt" DESC, "dispute_status_history"."id" DESC
