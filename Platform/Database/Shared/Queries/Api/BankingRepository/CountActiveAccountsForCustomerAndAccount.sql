SELECT
    COUNT(*)
FROM "accounts"."accounts" AS account
JOIN "identity"."users" AS owner ON owner.id = account."userId"
WHERE account."publicId" = @Account
AND owner."publicId" = @Customer
AND account."accountStatus" = 'Active'
AND account."currencyCode" = @Currency
