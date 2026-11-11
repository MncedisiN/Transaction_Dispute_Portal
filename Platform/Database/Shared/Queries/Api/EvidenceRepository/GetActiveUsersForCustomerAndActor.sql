SELECT
    DISTINCT "u"."publicId" AS "id",
    "p"."displayName" AS DisplayName
FROM "identity"."users" u
JOIN "identity"."customerProfiles" p ON "p"."userId" = "u"."id"
JOIN "identity"."userRoles" ur ON "ur"."userId" = "u"."id"
JOIN "identity"."roles" r ON "r"."id" = "ur"."roleId"
WHERE "u"."accountStatus" = 'Active'
AND "u"."publicId" <> @Actor
AND "u"."publicId" <> @Customer
AND("r"."code" IN ('super_admin', 'operations_manager')
    OR("u"."publicId" = @Assigned
        AND "r"."code" = @Specialist))
ORDER BY "p"."displayName", "u"."publicId"
