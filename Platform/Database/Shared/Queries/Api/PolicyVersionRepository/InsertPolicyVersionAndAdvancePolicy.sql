INSERT INTO configuration."{{Table}}Policies"(
    "policyId",
    "supersedesVersionId",
    "createdById",
    "publicId",
    version,
    priority,
    "effectiveFromUtc",
    "effectiveToUtc",
    "publishedAtUtc",
    "createdAtUtc",
    "changeReason",
    "requestId",
    "requestFingerprint",
    "sourceVersionPublicId",
    provenance,
    {{SelectedColumns}}
)
VALUES (
    @Policy,
    (
        SELECT
            old.id
        FROM configuration."{{RelatedTable}}Policies" old
        WHERE old."policyId" = @Policy
        AND old.version = @LastVersion),
    (
        SELECT
            id
        FROM "identity".users
        WHERE "publicId" = @Actor),
    @VersionPublicId,
    @Version,
    @Priority,
    @EffectiveFromUtc,
    @EffectiveToUtc,
    @PublishedAtUtc,
    @CreatedAtUtc,
    @ChangeReason,
    @RequestId,
    @Fingerprint,
    @SourceVersionPublicId,
    @Provenance,
    {{Values}}
);

UPDATE configuration."{{AdditionalTable}}Policies"
SET
    "lastVersion" = @Version,
    "concurrencyToken" = @ConcurrencyToken
WHERE id = @Policy;
