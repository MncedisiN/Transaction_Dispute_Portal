UPDATE accounts."accounts"
SET
    status = CASE WHEN "countryCode" <> @Country
OR currency <> @Currency THEN 'Blocked' ELSE status END,
    "countryPolicyVersion" = @PolicyVersion
WHERE status IN ('Active', 'PendingLedger')
AND "countryPolicyVersion" <= @PolicyVersion
AND("countryPolicyVersion" < @PolicyVersion
    OR "countryCode" <> @Country
    OR currency <> @Currency)
