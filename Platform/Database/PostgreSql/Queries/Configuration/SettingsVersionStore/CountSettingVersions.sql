SELECT
    COUNT(*)
FROM configuration."settings" v
JOIN configuration."settings" p ON p.id = v."policyId"
    AND p.version = 0
    AND v.version > 0 {{Filter}}
