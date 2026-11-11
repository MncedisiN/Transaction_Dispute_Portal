SELECT
    COUNT(*)
FROM "identity"."accountDeletionChallenges" AS "account_deletion_challenges"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "account_deletion_challenges"."userId") = @User
AND "account_deletion_challenges"."createdAt" > @Since
