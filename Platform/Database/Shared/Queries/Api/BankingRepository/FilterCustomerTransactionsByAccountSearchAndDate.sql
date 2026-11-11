WHERE (
    SELECT
        "related1"."publicId"
    FROM "identity"."users" AS "related1"
    WHERE "related1"."id" = "a"."userId") = @CustomerId
AND "t"."archivedAt" IS NULL
AND "t"."transactionAt" >= "a"."createdAt"
AND "t"."transactionAt" < {{AccountOpeningWindowEnd}}
AND("a"."accountType" IN ('Cheque', 'Savings')
    OR EXISTS (
        SELECT
            1
        FROM "integration"."accountMigrations" m
        WHERE "m"."accountId" = "a"."id"
        AND "m"."state" = 'Active'))
AND(@AccountId IS NULL
    OR(
        SELECT
            "related2"."publicId"
        FROM "accounts"."accounts" AS "related2"
        WHERE "related2"."id" = "t"."accountId") = @AccountId)
AND(@SearchPattern IS NULL
    OR {{SearchPredicate}}
    OR {{RelatedSearchPredicate}}
    OR {{AdditionalSearchPredicate}})
AND(@FromDate IS NULL
    OR "t"."transactionAt" >= @FromDate)
AND(@ToDateExclusive IS NULL
    OR "t"."transactionAt" < @ToDateExclusive)
AND(@Direction IS NULL
    OR "t"."direction" = @Direction)
AND(@Type IS NULL
    OR(
        SELECT
            "related3"."code"
        FROM "configuration"."transactionTypes" AS "related3"
        WHERE "related3"."id" = "t"."transactionTypeId") = @Type)
