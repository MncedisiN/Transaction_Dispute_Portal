UPDATE "accounts"."accounts"
SET
    "accountStatus" = CASE WHEN "currencyCode" = @Currency THEN "accountStatus" ELSE 'Blocked' END,
    "countryPolicyVersion" = @Version
WHERE "accountStatus" = 'Active'
AND "countryPolicyVersion" <= @Version
