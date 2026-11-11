SELECT
    COUNT(*)
FROM "identity"."users" u
JOIN "identity"."userRoles" ur ON "ur"."userId" = "u"."id"
JOIN "identity"."roles" r ON "r"."id" = "ur"."roleId"
WHERE "r"."code" = 'super_admin'
AND "u"."accountStatus" = 'Active'
