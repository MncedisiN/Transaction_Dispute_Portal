INSERT INTO "audit"."auditEvents"(
    "publicId",
    "actorUserId",
    "action",
    "entityType",
    "entityPublicId",
    "correlationId",
    "outcome",
    "occurredAt",
    "safeMetadata"
)
VALUES (
    @Id,
    (
        SELECT
            "resolved1"."id"
        FROM "identity"."users" AS "resolved1"
        WHERE "resolved1"."publicId" = @Actor),
    'SupportConversationClosed',
    'operations',
    @Thread,
    @Correlation,
    'Success',
    @Now,
    {{AuditMetadataValueExpression}}
)
