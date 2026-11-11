SELECT
    "account_deletion_challenges"."codeHash" AS CodeHash,
    "account_deletion_challenges"."state" AS "state",
    "account_deletion_challenges"."attempts" AS "attempts",
    "account_deletion_challenges"."expiresAt" AS ExpiresAt
FROM "identity"."accountDeletionChallenges" AS "account_deletion_challenges"
WHERE "account_deletion_challenges"."publicId" = @Id
AND(
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "account_deletion_challenges"."userId") = @User
