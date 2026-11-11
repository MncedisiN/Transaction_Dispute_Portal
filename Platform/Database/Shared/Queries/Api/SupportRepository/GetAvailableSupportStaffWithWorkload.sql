SELECT
    DISTINCT "u"."publicId" AS "id",
    COALESCE("s"."capacity", 0) AS Capacity,
    "s"."lastAssignedAt" AS LastAssignedAt,
    (
    SELECT
        COUNT(*)
    FROM "disputes"."disputes" active_case
    WHERE "active_case"."assignedToId" = "u"."id"
    AND "active_case"."status" NOT IN ('Resolved', 'Declined', 'Withdrawn')) + (
    SELECT
        COUNT(*)
    FROM "support"."conversations" active_chat
    WHERE "active_chat"."assignedToId" = "u"."id"
    AND "active_chat"."conversationStatus" = 'Open'
    AND "active_chat"."disputeId" IS NULL) AS Workload
FROM "identity"."users" u
JOIN "identity"."userRoles" ur ON "ur"."userId" = "u"."id"
JOIN "identity"."roles" r ON "r"."id" = "ur"."roleId"
JOIN "support"."staffCapacity" s ON "s"."userId" = "u"."id"
WHERE "u"."accountStatus" = 'Active'
AND "s"."availability" = 'Available'
AND {{PermissionPredicate}}
