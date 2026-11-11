SELECT
    "d"."publicId" AS "id",
    "d"."reference" AS "reference",
    "d"."status" AS "status",
    "t"."description" AS TransactionDescription,
    "t"."merchantName" AS MerchantName,
    "d"."createdAt" AS CreatedAt {{FilterClause}}
ORDER BY CASE WHEN "d"."status" IN ('Resolved', 'Declined', 'Withdrawn') THEN 1 ELSE 0 END, "d"."priority" DESC, "d"."createdAt" DESC, "d"."id" DESC {{PaginationClause}};

SELECT
    COUNT(*) {{RelatedFilterClause}};
