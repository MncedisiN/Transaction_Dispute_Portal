SELECT
    COUNT(*)
FROM configuration."{{Table}}Policies" v
JOIN configuration."{{RelatedTable}}Policies" p ON p.id = v."policyId"
WHERE p.id <> @Policy
AND p.scope = @Scope
AND v.priority = @Priority
AND v."isDeleted" = @Deleted
AND v."publishedAtUtc" IS NOT NULL
AND(@Until IS NULL
    OR v."effectiveFromUtc" < @Until)
AND(v."effectiveToUtc" IS NULL
    OR @From < v."effectiveToUtc")
AND NOT EXISTS (
    SELECT
        1
    FROM configuration."{{AdditionalTable}}Policies" replacement
    WHERE replacement."policyId" = p.id
    AND replacement.version > v.version
    AND replacement."effectiveFromUtc" <= @From
    AND replacement."publishedAtUtc" IS NOT NULL)
