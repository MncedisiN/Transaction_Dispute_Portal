{{ThreadSelect}} {{FilterClause}}
ORDER BY LastMessageAt DESC, "st"."publicId" {{PaginationClause}};

SELECT
    COUNT(*) {{ThreadFrom}} {{RelatedFilterClause}};
