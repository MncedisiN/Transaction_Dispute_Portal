INSERT INTO "audit"."auditEvents"(
    "publicId",
    "actorUserId",
    "action",
    "entityType",
    "entityPublicId",
    "correlationId",
    "safeMetadata"
)
VALUES (
    @Id,
    (
        SELECT
            "resolved1"."id"
        FROM "identity"."users" AS "resolved1"
        WHERE "resolved1"."publicId" = @ActorId),
    @Action,
    @EntityType,
    @EntityId,
    @CorrelationId,
    {{AuditMetadataValueExpression}}
)
