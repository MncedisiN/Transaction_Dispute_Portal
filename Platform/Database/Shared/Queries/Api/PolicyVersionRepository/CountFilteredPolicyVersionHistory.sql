SELECT
    COUNT(*)
FROM configuration."{{Table}}Policies" v
JOIN configuration."{{RelatedTable}}Policies" p ON p.id = v."policyId" {{FilterClause}}
