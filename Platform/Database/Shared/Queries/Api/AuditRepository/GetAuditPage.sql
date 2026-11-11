{{SelectQuery}} {{FilterClause}}
ORDER BY a."occurredAt" DESC, a.id DESC {{PaginationClause}};

SELECT
    COUNT(*) {{RelatedFilterClause}}
