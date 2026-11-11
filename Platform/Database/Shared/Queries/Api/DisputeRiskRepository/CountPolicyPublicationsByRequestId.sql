SELECT
    (
    SELECT
        COUNT(*)
    FROM configuration."riskPolicies"
    WHERE "requestId" = @Request) + (
    SELECT
        COUNT(*)
    FROM configuration."refundPolicies"
    WHERE "requestId" = @Request)
