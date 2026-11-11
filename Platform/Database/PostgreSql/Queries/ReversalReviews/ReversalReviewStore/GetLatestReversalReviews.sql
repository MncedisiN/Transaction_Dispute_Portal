SELECT
    "publicId" AS id,
    status,
    "requestBody" ->> 'originalTransactionId' AS originalTransactionId,
    "requestBody" ->> 'kind' AS kind,
    "decisionCode" AS decisionCode,
    "createdAt" AS createdAt
FROM debt."reversalReviews"
ORDER BY "createdAt" DESC
LIMIT 250
