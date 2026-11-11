SELECT
    a."publicId" AS Id,
    a."maskedAccountNumber" AS Masked,
    a."protectedNumber" AS ProtectedNumber,
    a."accountStatus" AS Status,
    {{Local}} AS IsLocal
FROM accounts.accounts a
JOIN "identity".users account_owner ON account_owner.id = a."userId"
WHERE account_owner."publicId" = @Owner
AND {{MembershipPredicate}}
AND(@ActiveOnly = 0
    OR a."accountStatus" = 'Active')
