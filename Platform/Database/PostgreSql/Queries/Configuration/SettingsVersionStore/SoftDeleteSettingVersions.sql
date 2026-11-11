UPDATE configuration."settings"
SET
    "isDeleted" = TRUE,
    "deletedAtUtc" = @At,
    "deletedByPublicId" = @Actor,
    "deletionReason" = @Reason
WHERE "publicId" = @Version;

UPDATE configuration."settings"
SET
    "concurrencyToken" = "concurrencyToken" + 1
WHERE code = 'Default';
