UPDATE "identity"."disputeVerifications"
SET
    "state" = 'Used'
WHERE "disputeVerifications"."publicId" = @Id
AND(
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "disputeVerifications"."userId") = @User
AND(
    SELECT
        "related2"."publicId"
    FROM "identity"."userSessions" AS "related2"
    WHERE "related2"."id" = "disputeVerifications"."sessionId") = @Session
AND(
    SELECT
        "related3"."publicId"
    FROM "transactions"."transactions" AS "related3"
    WHERE "related3"."id" = "disputeVerifications"."transactionId") = @Transaction
AND "disputeVerifications"."policyVersion" = @Version
AND "disputeVerifications"."riskPolicyVersionId" = (
    SELECT
        id
    FROM configuration."riskPolicies"
    WHERE "publicId" = @PolicySnapshot)
AND "disputeVerifications"."state" = 'Verified'
AND "disputeVerifications"."expiresAt" > @Now
AND EXISTS (
    SELECT
        1
    FROM "identity"."userSessions" s
    WHERE "s"."publicId" = @Session
    AND(
        SELECT
            "related4"."publicId"
        FROM "identity"."users" AS "related4"
        WHERE "related4"."id" = "s"."userId") = @User
    AND "s"."revokedAt" IS NULL
    AND "s"."expiresAt" > @Now)
