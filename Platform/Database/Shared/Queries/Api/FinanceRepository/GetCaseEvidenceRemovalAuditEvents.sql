SELECT
    removal_event."publicId" AS id,
    'Audit' AS Source,
    'EvidenceRemoved' AS Action,
    removal_event."occurredAt" AS OccurredAt,
    NULL AS Detail
FROM audit."auditEvents" removal_event
JOIN disputes.evidence e ON e."publicId" = removal_event."entityPublicId"
JOIN disputes.disputes case_record ON case_record.id = e."disputeId"
WHERE removal_event.action = 'RecordSoftDeleted'
AND removal_event."entityType" = 'evidence'
AND case_record."publicId" = @Id
