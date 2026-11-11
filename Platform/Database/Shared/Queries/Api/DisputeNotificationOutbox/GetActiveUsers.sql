SELECT
    DISTINCT "u"."publicId" AS "id"
FROM "identity"."users" u
JOIN "identity"."userRoles" ur ON "ur"."userId" = "u"."id"
JOIN "identity"."roles" r ON "r"."id" = "ur"."roleId"
WHERE "u"."accountStatus" = 'Active'
AND("r"."code" IN ('super_admin', 'operations_manager')
    OR "r"."code" = @Specialist)
