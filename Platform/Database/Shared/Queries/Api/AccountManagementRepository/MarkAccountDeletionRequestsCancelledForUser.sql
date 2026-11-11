UPDATE "identity"."accountDeletionRequests"
SET
    "status" = 'Cancelled',
    "version" = "accountDeletionRequests"."version" + 1
WHERE "accountDeletionRequests"."publicId" = @Id
AND(
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "accountDeletionRequests"."userId") = @User
AND "accountDeletionRequests"."status" IN ('Pending', 'UnderReview')
