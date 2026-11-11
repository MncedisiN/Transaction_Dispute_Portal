SELECT
    DISTINCT "u"."publicId" AS "id"
FROM "identity"."users" u
JOIN "identity"."userRoles" ur ON "ur"."userId" = "u"."id"
JOIN "identity"."roles" r ON "r"."id" = "ur"."roleId"
JOIN "disputes"."disputes" d ON "d"."publicId" = @Id
WHERE "u"."accountStatus" = 'Active'
AND("r"."code" IN ('super_admin', 'operations_manager')
    OR "u"."id" = "d"."assignedToId")
