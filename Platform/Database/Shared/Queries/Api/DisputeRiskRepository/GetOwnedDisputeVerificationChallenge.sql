SELECT
    "dispute_verifications"."publicId" AS "id",
    "dispute_verifications"."policyVersion" AS PolicyVersion,
    "dispute_verifications"."channel" AS "channel",
    "dispute_verifications"."codeHash" AS CodeHash,
    "dispute_verifications"."state" AS "state",
    "dispute_verifications"."attempts" AS "attempts",
    "dispute_verifications"."expiresAt" AS ExpiresAt
FROM "identity"."disputeVerifications" AS "dispute_verifications"
WHERE "dispute_verifications"."publicId" = @Id
AND(
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "dispute_verifications"."userId") = @User
AND(
    SELECT
        "related2"."publicId"
    FROM "identity"."userSessions" AS "related2"
    WHERE "related2"."id" = "dispute_verifications"."sessionId") = @Session
AND(
    SELECT
        "related3"."publicId"
    FROM "transactions"."transactions" AS "related3"
    WHERE "related3"."id" = "dispute_verifications"."transactionId") = @Transaction
