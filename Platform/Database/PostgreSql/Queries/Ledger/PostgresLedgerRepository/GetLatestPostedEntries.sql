SELECT
    e."publicId" AS id,
    j."transactionPublicId" AS transactionId,
    j.reference,
    e.currency,
    e.side,
    e.amount,
    e."createdAt" AS createdAt
FROM ledger."entries" e
JOIN ledger."journals" j ON j.id = e."journalId"
WHERE e."accountId" = @Id
AND j.status = 'Posted'
ORDER BY e."createdAt" DESC, e."publicId" DESC
LIMIT 250
