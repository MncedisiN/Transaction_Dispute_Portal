SELECT
    'Audit' AS source,
    audit_event."publicId" AS id,
    audit_event.action AS action,
    audit_event."entityType" AS "entityType",
    audit_event."entityPublicId" AS "entityId",
    audit_event."occurredAt" AS "occurredAt",
    audit_event."correlationId" AS "correlationId",
    {{AuditMetadataSelectExpression}} AS "safeMetadata",
    audit_event.outcome AS outcome
FROM audit."auditEvents" audit_event
UNION ALL SELECT
    'Security' AS source,
    security_event."publicId" AS id,
    security_event."eventType" AS action,
    'security' AS "entityType",
    subject."publicId" AS "entityId",
    security_event."occurredAt" AS "occurredAt",
    security_event."correlationId" AS "correlationId",
    '{}' AS "safeMetadata",
    security_event.outcome AS outcome
FROM "identity"."securityEvents" security_event
LEFT JOIN "identity".users subject ON subject.id = security_event."userId"
