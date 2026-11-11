SELECT
    "d"."publicId" AS "id",
    (
    SELECT
        owner_user."publicId"
    FROM "identity".users owner_user
    WHERE owner_user.id = d."customerId") AS CustomerId,
    "d"."reference" AS "reference",
    "d"."status" AS "status",
    "d"."queueType" AS QueueType,
    "d"."severity" AS "severity",
    CAST("d"."priority" AS int) AS Priority,
    "d"."version" AS "version",
    (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "d"."assignedToId") AS AssignedTo,
    "d"."autoAssignmentPaused" AS AutoAssignmentPaused,
    CASE WHEN NULLIF("p"."firstName", '') IS NOT NULL
AND NULLIF("p"."lastName", '') IS NOT NULL THEN CONCAT("p"."firstName", ' ', "p"."lastName") ELSE NULL END AS AssignedName,
    "d"."createdAt" AS CreatedAt,
    "d"."assignedAt" AS AssignedAt,
    "d"."responseDueAt" AS ResponseDueAt,
    "d"."dueAt" AS DueAt,
    "d"."firstResponseAt" AS FirstResponseAt,
    "d"."slaClosedAt" AS SlaClosedAt,
    "d"."escalationLevel" AS EscalationLevel,
    "d"."escalatedAt" AS EscalatedAt
FROM "disputes"."disputes" d
LEFT JOIN "identity"."customerProfiles" p ON "p"."userId" = "d"."assignedToId"
WHERE "d"."publicId" = @Id
AND {{StaffScopeSql}}
