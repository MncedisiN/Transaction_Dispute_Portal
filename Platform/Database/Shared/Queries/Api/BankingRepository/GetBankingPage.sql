{{TransactionSelect}} {{FilterClause}}
ORDER BY {{OrderBy}} {{PaginationClause}};

SELECT
    COUNT(*)
FROM "transactions"."transactions" t
JOIN "accounts"."accounts" a ON "a"."id" = "t"."accountId" {{RelatedFilterClause}};
