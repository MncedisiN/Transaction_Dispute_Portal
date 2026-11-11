UPDATE "integration"."localBankingIdentities"
SET
    "identityHash" = @Hash
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "localBankingIdentities"."userId") = @User
AND "localBankingIdentities"."identityHash" IS NULL
