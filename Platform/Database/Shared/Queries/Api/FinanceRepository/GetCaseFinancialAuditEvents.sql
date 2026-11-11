SELECT
    "a"."publicId" AS "id",
    'Audit' AS Source,
    "a"."action" AS "action",
    "a"."occurredAt" AS OccurredAt,
    NULL AS Detail,
    {{AuditMetadataSelectExpression}} AS SafeMetadata
FROM audit."auditEvents" a
WHERE a."entityPublicId" = @Id
AND {{AuditActions}}
