SELECT
    COUNT(*)
FROM "identity"."accountDeletionRequests" AS "account_deletion_requests"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "account_deletion_requests"."userId") = @User
AND "account_deletion_requests"."status" IN ('Pending', 'UnderReview')
