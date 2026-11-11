INSERT INTO "accounts"."bankLinks"(
    "accountId",
    provider,
    "externalAccountHash",
    "protectedExternalAccount"
)
SELECT
    account.id,
    @Provider,
    @Hash,
    @Protected
FROM "accounts"."accounts" AS account
WHERE account."publicId" = @Account
