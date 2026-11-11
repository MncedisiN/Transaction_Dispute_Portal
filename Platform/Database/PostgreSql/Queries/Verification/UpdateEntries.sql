UPDATE ledger.entries
SET
    amount = amount + 1
WHERE "journalId" IN (
    SELECT
        id
    FROM ledger.journals
    WHERE "transactionPublicId" = @Id)
