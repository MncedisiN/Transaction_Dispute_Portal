SELECT
    COUNT(*)
FROM "identity".users AS u
LEFT JOIN "identity"."customerProfiles" AS p ON p."userId" = u.id
WHERE u."normalizedLoginIdentifier" = @Normalized
OR UPPER(TRIM(p.email)) = @Normalized
