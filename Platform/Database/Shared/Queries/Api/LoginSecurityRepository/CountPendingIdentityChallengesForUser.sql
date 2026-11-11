SELECT
    COUNT(*)
FROM "identity"."identityChallenges" AS "identity_challenges"
WHERE "identity_challenges"."publicId" = @Id
AND(
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "identity_challenges"."userId") = @User
AND "identity_challenges"."purpose" = 'Login'
AND "identity_challenges"."state" = 'Pending'
AND "identity_challenges"."expiresAt" > @Now
