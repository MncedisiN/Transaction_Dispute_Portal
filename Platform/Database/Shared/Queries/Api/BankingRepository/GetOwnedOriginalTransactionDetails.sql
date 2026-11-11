{{TransactionSelect}}
WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "a"."userId") = @CustomerId
AND "t"."publicId" = @TransactionId
