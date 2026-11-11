INSERT INTO "identity"."disputeVerifications"(
    "publicId",
    "userId",
    "sessionId",
    "transactionId",
    "policyVersion",
    "riskPolicyVersionId",
    "channel",
    "codeHash",
    "state",
    "createdAt",
    "expiresAt"
)
VALUES (
    @Id,
    (
        SELECT
            "resolved1"."id"
        FROM "identity"."users" AS "resolved1"
        WHERE "resolved1"."publicId" = @User),
    (
        SELECT
            "resolved2"."id"
        FROM "identity"."userSessions" AS "resolved2"
        WHERE "resolved2"."publicId" = @Session),
    (
        SELECT
            "resolved3"."id"
        FROM "transactions"."transactions" AS "resolved3"
        WHERE "resolved3"."publicId" = @Transaction),
    @Version,
    (
        SELECT
            v.id
        FROM configuration."riskPolicies" v
        WHERE v."publicId" = @PolicySnapshot),
    @Channel,
    @Hash,
    'Pending',
    @Now,
    @Expires
)
