SELECT
    "requestFingerprint"
FROM configuration."{{Table}}Policies"
WHERE "requestId" = @Request
AND "policyId" = @Policy
