UPDATE migration
SET
    state = 'Active',
    "activatedAt" = @Now
FROM "integration"."accountMigrations" migration
JOIN "accounts"."accounts" account ON account.id = migration."accountId"
WHERE account."publicId" = @AccountId
AND migration."sourceHash" = @Hash
