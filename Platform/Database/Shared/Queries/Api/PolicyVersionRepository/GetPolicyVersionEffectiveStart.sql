SELECT
    "effectiveFromUtc"
FROM configuration."{{Table}}Policies"
WHERE "policyId" = @Policy
AND version = @Version
