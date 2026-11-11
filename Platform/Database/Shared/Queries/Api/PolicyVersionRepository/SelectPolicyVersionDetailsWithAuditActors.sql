SELECT
    p."publicId" AS PolicyPublicId,
    v."publicId" AS VersionPublicId,
    v.version AS Version,
    p."concurrencyToken" AS ConcurrencyToken,
    p.scope AS Scope,
    v.priority AS Priority,
    v."effectiveFromUtc" AS EffectiveFromUtc,
    v."effectiveToUtc" AS EffectiveToUtc,
    v."publishedAtUtc" AS PublishedAtUtc,
    v."createdAtUtc" AS CreatedAtUtc,
    creator."publicId" AS CreatedBy,
    COALESCE((
        SELECT
            snapshot."accountName"
        FROM audit."actorSnapshots" snapshot
        JOIN audit."auditEvents" audit_entry ON audit_entry.id = snapshot."auditEventId"
        WHERE audit_entry."entityPublicId" = v."publicId"
        AND audit_entry.action = 'PolicyVersionPublished'), creator_profile."displayName") AS CreatedByName,
    v."changeReason" AS ChangeReason,
    v."isDeleted" AS IsDeleted,
    v."deletedAtUtc" AS DeletedAtUtc,
    remover."publicId" AS DeletedBy,
    COALESCE((
        SELECT
            snapshot."accountName"
        FROM audit."actorSnapshots" snapshot
        JOIN audit."auditEvents" audit_entry ON audit_entry.id = snapshot."auditEventId"
        WHERE audit_entry."entityPublicId" = v."publicId"
        AND audit_entry.action = 'PolicyVersionDeleted'), remover_profile."displayName") AS DeletedByName,
    v."deletionReason" AS DeletionReason,
    v.provenance AS Provenance,
    v."requestId" AS RequestId,
    v."sourceVersionPublicId" AS SourceVersionPublicId,
    {{Fields}}
FROM configuration."{{Table}}Policies" v
JOIN configuration."{{RelatedTable}}Policies" p ON p.id = v."policyId" AND v."version" > 0
LEFT JOIN "identity".users creator ON creator.id = v."createdById"
LEFT JOIN "identity"."customerProfiles" creator_profile ON creator_profile."userId" = creator.id
LEFT JOIN "identity".users remover ON remover.id = v."deletedById"
LEFT JOIN "identity"."customerProfiles" remover_profile ON remover_profile."userId" = remover.id
