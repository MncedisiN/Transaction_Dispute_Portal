SELECT
    COUNT(*)
FROM sys.columns
WHERE name IN ('amount', 'currentDelta', 'reservedDelta')
AND scale = 8
AND object_id IN (OBJECT_ID('transactions.transactions'), OBJECT_ID('ledger.projectionEntries'), OBJECT_ID('disputes.refunds'), OBJECT_ID('disputes.refundRecalls'))
