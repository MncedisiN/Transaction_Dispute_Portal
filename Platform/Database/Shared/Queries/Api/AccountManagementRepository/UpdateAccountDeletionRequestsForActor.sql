UPDATE "identity"."accountDeletionRequests"
SET
    "status" = @Status,
    "version" = "accountDeletionRequests"."version" + 1,
    "protectedDecision" = @Decision,
    "reviewedAt" = @Now,
    "reviewedById" = (
    SELECT
        "resolved1"."id"
    FROM "identity"."users" AS "resolved1"
    WHERE "resolved1"."publicId" = @Actor)
WHERE "accountDeletionRequests"."publicId" = @Id
