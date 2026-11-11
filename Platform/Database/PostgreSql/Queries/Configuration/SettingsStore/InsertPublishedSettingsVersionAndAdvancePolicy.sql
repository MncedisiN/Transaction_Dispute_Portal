INSERT INTO configuration."settings"(
    "policyId",
    "supersedesVersionId",
    "publicId",
    version,
    "effectiveFromUtc",
    "publishedAtUtc",
    "createdAtUtc",
    "createdByPublicId",
    "changeReason",
    "requestId",
    "requestFingerprint",
    "sourceVersionPublicId",
    provenance,
    body
)
SELECT
    p.id,
    (
    SELECT
        old.id
    FROM configuration."settings" old
    WHERE old."policyId" = p.id
    AND old.version = p."lastVersion"),
    @Id,
    @Version,
    @At,
    @At,
    @At,
    @Actor,
    @Reason,
    @Request,
    @Hash,
    @Source,
    'Published',
    CAST(@Body AS jsonb)
FROM configuration."settings" p
WHERE p.code = 'Default';

UPDATE configuration."settings"
SET
    "lastVersion" = @Version,
    "concurrencyToken" = "concurrencyToken" + 1
WHERE code = 'Default';
