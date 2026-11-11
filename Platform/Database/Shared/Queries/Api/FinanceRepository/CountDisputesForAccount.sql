SELECT
    COUNT(*)
FROM "disputes"."disputes" d
JOIN "transactions"."transactions" t ON "t"."id" = "d"."transactionId"
WHERE (
    SELECT
        "related1"."publicId"
    FROM "accounts"."accounts" AS "related1"
    WHERE "related1"."id" = "t"."accountId") = @Account
AND(
    SELECT
        "related2"."code"
    FROM "configuration"."disputeCategories" AS "related2"
    WHERE "related2"."id" = "d"."categoryId") = 'Unrecognized'
AND "d"."status" IN ('Submitted', 'UnderReview', 'AwaitingCustomer')
