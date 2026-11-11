INSERT INTO "identity"."loginVerificationFlows"(
    "publicId",
    "challengeId",
    "userId",
    "flowHash",
    "passwordStamp",
    "agentHash",
    "protectedDestination",
    "verifyRegisteredContact",
    "createdAt",
    "expiresAt",
    "resendAvailableAt"
)
SELECT
    "identity_challenges"."publicId",
    "identity_challenges"."id",
    "identity_challenges"."userId",
    @Flow,
    @Password,
    @Agent,
    @Destination,
    @Verify,
    @Now,
    @Expires,
    @Resend
FROM "identity"."identityChallenges" AS "identity_challenges"
WHERE "identity_challenges"."publicId" = @Id
AND "identity_challenges"."userId" = (
    SELECT
        "users"."id" AS "row_id"
    FROM "identity"."users" AS "users"
    WHERE "users"."publicId" = @User)
AND "identity_challenges"."purpose" = 'Login'
