UPDATE accounts."accounts"
SET
    status = 'Active',
    "countryPolicyVersion" = @PolicyVersion
WHERE "publicId" = @AccountId
AND "customerPublicId" = @UserId
AND "countryCode" = @CountryCode
AND currency = @Currency
AND status = 'PendingLedger'
AND "countryPolicyVersion" <= @PolicyVersion
