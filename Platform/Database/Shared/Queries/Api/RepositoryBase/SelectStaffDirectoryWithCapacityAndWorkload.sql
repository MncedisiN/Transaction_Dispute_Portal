SELECT
    "u"."publicId" AS "id",
    "p"."firstName" AS FirstName,
    "p"."middleName" AS MiddleName,
    "p"."lastName" AS LastName,
    "p"."displayName" AS DisplayName,
    COALESCE("p"."email", "u"."loginIdentifier") AS Email,
    "r"."code" AS Role,
    "u"."accountStatus" AS Status,
    COALESCE("s"."availability", 'Offline') AS Availability,
    COALESCE("s"."capacity", 0) AS Capacity,
    (
    SELECT
        COUNT(*)
    FROM "disputes"."disputes" d
    WHERE "d"."assignedToId" = "u"."id"
    AND "d"."status" NOT IN ('Resolved', 'Declined', 'Withdrawn')) + (
    SELECT
        COUNT(*)
    FROM "support"."conversations" active_support
    WHERE "active_support"."assignedToId" = "u"."id"
    AND "active_support"."conversationStatus" = 'Open'
    AND "active_support"."disputeId" IS NULL) AS ActiveCases,
    "s"."lastAssignedAt" AS LastAssignedAt
FROM "identity"."users" u
JOIN "identity"."customerProfiles" p ON "p"."userId" = "u"."id"
JOIN "identity"."userRoles" ur ON "ur"."userId" = "u"."id"
JOIN "identity"."roles" r ON "r"."id" = "ur"."roleId"
LEFT JOIN "support"."staffCapacity" s ON "s"."userId" = "u"."id"
