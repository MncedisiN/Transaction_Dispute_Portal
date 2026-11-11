SELECT 
    a."id" AS AccountKey,
    u."publicId" AS UserId
FROM "accounts"."accounts" AS a
INNER JOIN "identity"."users" AS u
    ON u."id" = a."userId"
WHERE a."id" > @After
    AND a."accountStatus" = 'Active'
    AND a."deletedAt" IS NULL
    AND u."deletedAt" IS NULL
    AND NOT EXISTS (
        SELECT 1
        FROM "transactions"."transactions" AS t
        WHERE t."accountId" = a."id"
    )
ORDER BY a."id"
LIMIT @BatchSize;
