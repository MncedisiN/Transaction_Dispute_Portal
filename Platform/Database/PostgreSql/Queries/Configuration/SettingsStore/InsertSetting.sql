WITH family_identity AS (
    SELECT nextval(pg_get_serial_sequence('configuration."settings"', 'id')) AS id
)
INSERT INTO configuration."settings" (
    "id", "policyId", "publicId", "version", "effectiveFromUtc", "changeReason",
    "provenance", "body", "configurationKey", "code", "lastVersion", "concurrencyToken"
)
SELECT
    id, id, gen_random_uuid(), 0, CURRENT_TIMESTAMP, 'Initial settings definition.',
    'InitialDefinition', CAST(@Body AS jsonb), id, 'Default', 1, 1
FROM family_identity
ON CONFLICT ("code") WHERE version = 0 DO NOTHING;
