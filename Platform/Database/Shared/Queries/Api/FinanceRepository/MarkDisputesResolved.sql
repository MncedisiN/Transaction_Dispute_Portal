UPDATE "disputes"."disputes"
SET
    "status" = 'Resolved',
    "resolution" = @Reason,
    "resolvedAt" = @Now,
    "updatedAt" = @Now,
    "firstResponseAt" = COALESCE("disputes"."firstResponseAt", @Now),
    "slaClosedAt" = @Now,
    "version" = "disputes"."version" + 1
WHERE "disputes"."publicId" = @Id
AND "disputes"."version" = @Version
