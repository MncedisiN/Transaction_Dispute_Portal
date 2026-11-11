UPDATE "disputes"."disputes"
SET
    "responseDueAt" = @Response,
    "dueAt" = @Due,
    "escalationLevel" = "disputes"."escalationLevel" + 1,
    "escalatedAt" = @Now,
    "escalationReason" = @Reason,
    "priority" = 5,
    "version" = "disputes"."version" + 1,
    "updatedAt" = @Now
WHERE "disputes"."publicId" = @Id
