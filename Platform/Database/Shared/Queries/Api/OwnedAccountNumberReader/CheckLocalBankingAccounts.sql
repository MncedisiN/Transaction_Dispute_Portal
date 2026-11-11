SELECT
    1
FROM integration."localBankingAccounts" local_account
JOIN integration."localBankingIdentities" bank_owner ON bank_owner.id = local_account."bankingIdentityId"
WHERE local_account."accountId" = a.id
AND bank_owner."userId" = a."userId"
