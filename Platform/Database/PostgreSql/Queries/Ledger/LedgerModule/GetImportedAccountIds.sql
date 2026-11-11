SELECT
    account."publicId"
FROM ledger."bookImports" AS imported
INNER JOIN ledger."accounts" AS account ON account.id = imported."accountId"
WHERE account."publicId" = ANY(@AccountIds)
