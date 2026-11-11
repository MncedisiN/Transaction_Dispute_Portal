SELECT
    p."publicId" AS PolicyPublicId,
    v."publicId" AS VersionPublicId,
    v.version AS Version,
    p."concurrencyToken" AS ConcurrencyToken,
    'Global' AS Scope,
    v.priority AS Priority,
    v."effectiveFromUtc" AS EffectiveFromUtc,
    v."effectiveToUtc" AS EffectiveToUtc,
    v."publishedAtUtc" AS PublishedAtUtc,
    v."createdAtUtc" AS CreatedAtUtc,
    v."createdByPublicId" AS CreatedBy,
    v."changeReason" AS ChangeReason,
    v."isDeleted" AS IsDeleted,
    v."deletedAtUtc" AS DeletedAtUtc,
    v."deletedByPublicId" AS DeletedBy,
    v."deletionReason" AS DeletionReason,
    v.provenance AS Provenance,
    v."requestId" AS RequestId,
    v."sourceVersionPublicId" AS SourceVersionPublicId,
    v.body::text AS Body
FROM configuration."settings" v
JOIN configuration."settings" p ON p.id = v."policyId"
    AND p.version = 0
    AND v.version > 0
