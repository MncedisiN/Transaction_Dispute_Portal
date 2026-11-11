SELECT
    "g"."publicId" AS "id",
    (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "g"."userId") AS UserId,
    "p"."displayName" AS DisplayName,
    "a"."displayName" AS GrantedBy,
    "g"."expiresAt" AS ExpiresAt,
    "g"."revokedAt" AS RevokedAt
FROM "disputes"."evidenceAccessGrants" g
JOIN "identity"."customerProfiles" p ON "p"."userId" = "g"."userId"
JOIN "identity"."customerProfiles" a ON "a"."userId" = "g"."grantedById"
WHERE (
    SELECT
        "related2"."publicId"
    FROM "disputes"."evidence" AS "related2"
    WHERE "related2"."id" = "g"."evidenceId") = @Id
ORDER BY "g"."createdAt" DESC
OFFSET 0 ROWS FETCH NEXT 100 ROWS ONLY
