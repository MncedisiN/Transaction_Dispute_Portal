SELECT
    COUNT(*)
FROM "identity"."userRoles" ur
JOIN "identity"."roles" r ON r.id = ur."roleId"
JOIN "identity"."users" u ON u.id = ur."userId"
WHERE u."publicId" = @Id
AND u."accountStatus" = 'Active'
AND r.code = 'super_admin'
