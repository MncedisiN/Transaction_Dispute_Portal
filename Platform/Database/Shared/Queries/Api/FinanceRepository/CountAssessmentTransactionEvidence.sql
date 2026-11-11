SELECT
    COUNT(*)
FROM transactions."bankLinks" AS link
JOIN transactions.transactions AS payment
    ON payment.id = link."transactionId"
JOIN accounts.accounts AS account
    ON account.id = link."accountId"
    AND account.id = payment."accountId"
WHERE payment."publicId" = @Transaction
    AND account."publicId" = @Account
    AND link."externalTransactionHash" = @Hash
    AND EXISTS (
        SELECT 1
        FROM service_audit AS receipt
        WHERE receipt.action = 'AssessmentTransactionsImported'
            AND receipt.entity_id = @Account
            AND receipt.actor = @Customer
    );
