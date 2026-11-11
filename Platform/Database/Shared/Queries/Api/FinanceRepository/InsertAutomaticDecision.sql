INSERT INTO "disputes"."automaticDecisions"(
    "publicId",
    "disputeId",
    "ruleCode",
    "policyVersion",
    "refundPolicyVersionId",
    "outcome",
    "reason",
    "factsJson"
)
VALUES (
    @Id,
    (
        SELECT
            "resolved1"."id"
        FROM "disputes"."disputes" AS "resolved1"
        WHERE "resolved1"."publicId" = @Case),
    @Rule,
    @Version,
    (
        SELECT
            id
        FROM configuration."refundPolicies"
        WHERE "publicId" = @VersionPublicId),
    @Outcome,
    @Reason,
    {{AuditMetadataValueExpression}}
)
