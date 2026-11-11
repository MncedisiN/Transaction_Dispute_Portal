{{CaseSelect}}
WHERE "d"."queueType" = @Queue
AND(
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "d"."assignedToId") IS NULL
AND "d"."autoAssignmentPaused" = {{FalseValue}}
AND "d"."status" NOT IN ('Resolved', 'Declined', 'Withdrawn')
ORDER BY "d"."priority" DESC, "d"."dueAt", "d"."createdAt", "d"."publicId"
OFFSET 0 ROWS FETCH NEXT 100 ROWS ONLY
