SELECT
    COALESCE(bool_or("ledgerBalance" <> 0), FALSE) AS HasNonzeroBalances,
    COALESCE(bool_or("reservedBalance" <> 0), FALSE) AS HasReserves,
    COALESCE(bool_or(status = 'PendingLedger'), FALSE) AS HasUnfinishedOperations
FROM accounts."accounts"
