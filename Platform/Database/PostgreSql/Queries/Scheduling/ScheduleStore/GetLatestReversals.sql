SELECT
    "publicId" AS id,
    "originalTransactionPublicId" AS originalTransactionId,
    kind,
    "dueAt" AS dueAt,
    status,
    reason,
    "decisionCode" AS decisionCode,
    "createdAt" AS createdAt
FROM scheduling."reversals"
ORDER BY "dueAt" DESC
LIMIT 250
