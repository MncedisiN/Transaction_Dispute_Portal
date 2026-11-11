SELECT
    "account_deletion_requests"."publicId" AS "id",
    (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "account_deletion_requests"."userId") AS UserId,
    "account_deletion_requests"."status" AS "status",
    "account_deletion_requests"."version" AS "version"
FROM "identity"."accountDeletionRequests" AS "account_deletion_requests"
WHERE "account_deletion_requests"."publicId" = @Id
