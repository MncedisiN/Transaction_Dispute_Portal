UPDATE ledger."accounts"
SET
    status = CASE WHEN "countryCode" <> @Country
OR currency <> @Currency THEN 'Blocked' ELSE status END,
    "countryPolicyVersion" = @PolicyVersion
WHERE kind = 'Customer'
AND status = 'Active'
AND "countryPolicyVersion" <= @PolicyVersion
AND("countryPolicyVersion" < @PolicyVersion
    OR "countryCode" <> @Country
    OR currency <> @Currency)
