UPDATE configuration."{{Table}}Policies"
SET
    "isDeleted" = @Deleted,
    "deletedAtUtc" = @At,
    "deletedById" = (
    SELECT
        id
    FROM "identity".users
    WHERE "publicId" = @Actor),
    "deletionReason" = @Reason
WHERE "publicId" = @Version;

UPDATE configuration."{{RelatedTable}}Policies"
SET
    "concurrencyToken" = "concurrencyToken" + 1
WHERE id = @Policy;
