UPDATE disputes.evidence
SET
    "deletedAt" = @Now,
    "deletedById" = (
    SELECT
        id
    FROM "identity".users
    WHERE "publicId" = @Actor),
    "deletionReasonProtected" = @Reason
WHERE "publicId" = @Evidence
AND "deletedAt" IS NULL;

UPDATE disputes."evidenceAccessGrants"
SET
    "revokedAt" = @Now,
    "revokedById" = (
    SELECT
        id
    FROM "identity".users
    WHERE "publicId" = @Actor),
    "revokeReasonProtected" = @Reason
WHERE "evidenceId" = (
    SELECT
        id
    FROM disputes.evidence
    WHERE "publicId" = @Evidence)
AND "revokedAt" IS NULL
