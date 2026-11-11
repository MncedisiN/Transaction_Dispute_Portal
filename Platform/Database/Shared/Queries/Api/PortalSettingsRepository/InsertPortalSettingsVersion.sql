INSERT INTO "configuration"."portalSettings" (
    "createdById",
    "publicId",
    "code",
    "version",
    "body",
    "changeReason",
    "createdAt"
)
SELECT
    "id",
    @Id,
    'Interface',
    @Version,
    {{SettingsBodyValue}},
    @Reason,
    @Now
FROM "identity"."users"
WHERE "publicId" = @Actor
