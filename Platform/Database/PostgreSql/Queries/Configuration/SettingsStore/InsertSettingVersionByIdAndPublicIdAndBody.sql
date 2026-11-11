INSERT INTO configuration."settings"(
    "policyId",
    "publicId",
    version,
    "effectiveFromUtc",
    "publishedAtUtc",
    "createdAtUtc",
    "changeReason",
    provenance,
    body
)
SELECT
    @Id,
    @PublicId,
    1,
    now(),
    now(),
    now(),
    'Initial explicit bootstrap configuration.',
    'Bootstrap',
    CAST(@Body AS jsonb)
WHERE NOT EXISTS (
    SELECT
        1
    FROM configuration."settings"
    WHERE "policyId" = @Id
        AND version > 0);
