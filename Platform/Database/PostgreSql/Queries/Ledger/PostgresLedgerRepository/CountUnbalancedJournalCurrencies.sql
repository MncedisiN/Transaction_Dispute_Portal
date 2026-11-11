SELECT
    COUNT(*)
FROM(
    SELECT
        "journalId",
        currency
    FROM ledger."entries"
    GROUP BY "journalId", currency
    HAVING SUM(CASE WHEN side = 'D' THEN amount ELSE - amount END) <> 0) x
