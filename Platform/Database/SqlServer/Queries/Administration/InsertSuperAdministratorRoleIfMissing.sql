IF NOT EXISTS (
    SELECT
        1
    FROM "identity"."userRoles" ur
    JOIN "identity"."roles" r ON r.id = ur."roleId"
    WHERE ur."userId" = @UserId
    AND r.code = 'super_admin')
INSERT "identity"."userRoles"(
    "userId",
    "roleId"
)
SELECT
    @UserId,
    id
FROM "identity"."roles"
WHERE code = 'super_admin';

UPDATE "identity"."userSessions"
SET
    "revokedAt" = @Now
WHERE "userId" = @UserId
AND "revokedAt" IS NULL
