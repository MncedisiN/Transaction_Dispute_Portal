SELECT
    COUNT(*)
FROM "identity"."contactVerificationChallenges" AS "contact_verification_challenges"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "contact_verification_challenges"."userId") = @User
AND "contact_verification_challenges"."createdAt" > @Since
