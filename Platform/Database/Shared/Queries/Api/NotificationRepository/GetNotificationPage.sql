{{SelectQuery}} {{FilterClause}}
ORDER BY "notifications"."createdAt" DESC, "notifications"."publicId" DESC {{PaginationClause}};

SELECT
    count(*) {{RelatedFilterClause}};
