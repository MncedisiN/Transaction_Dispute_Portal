{{SelectQuery}}
WHERE p.scope = 'Global'
AND(v."effectiveFromUtc" > @At
    OR v.version = (
        SELECT
            MAX(head.version)
        FROM configuration."{{Table}}Policies" head
        WHERE head."policyId" = p.id
        AND head."publishedAtUtc" IS NOT NULL
        AND head."effectiveFromUtc" <= @At))
