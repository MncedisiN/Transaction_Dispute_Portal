SELECT
    DISTINCT u.id AS Id,
    u."publicId" AS PublicId
FROM "identity"."users" u
JOIN "identity"."customerProfiles" p ON p."userId" = u.id
WHERE u."accountStatus" = 'Active'
AND(u."normalizedLoginIdentifier" = @Email
    OR UPPER(p.email) = @Email
    OR u."normalizedLoginIdentifier" = @Phone
    OR p.phone = @Phone)
