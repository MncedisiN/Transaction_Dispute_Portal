SELECT
    a."publicId" AS accountId,
    a.currency,
    a.balance,
    COALESCE(SUM(CASE WHEN e.side = 'C' THEN e.amount ELSE - e.amount END), 0) AS derivedBalance
FROM ledger."accounts" a
LEFT JOIN ledger."entries" e ON e."accountId" = a.id
GROUP BY a."publicId", a.currency, a.balance
HAVING a.balance <> COALESCE(SUM(CASE WHEN e.side = 'C' THEN e.amount ELSE - e.amount END), 0)
