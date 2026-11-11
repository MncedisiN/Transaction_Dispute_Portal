SELECT
    (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "account_deletion_requests"."userId") AS "user_id"
FROM "identity"."accountDeletionRequests" AS "account_deletion_requests"
WHERE "account_deletion_requests"."publicId" = @Id
