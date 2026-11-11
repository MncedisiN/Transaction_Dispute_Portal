SELECT
    COUNT(*)
FROM "integration"."localBankingIdentities" AS "demo_bank_identities"
WHERE "demo_bank_identities"."identityHash" = @Hash
AND(
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "demo_bank_identities"."userId") <> @User
