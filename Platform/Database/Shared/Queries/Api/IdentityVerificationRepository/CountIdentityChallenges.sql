SELECT
    COUNT(*)
FROM "identity"."identityChallenges" AS "identity_challenges"
WHERE "identity_challenges"."destinationHash" = @Hash
AND "identity_challenges"."purpose" = @Purpose
AND "identity_challenges"."createdAt" > @Since
