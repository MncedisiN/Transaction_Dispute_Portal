SELECT
    "publicId" AS "Id",
    "requestHash" AS RequestHash,
    "eventBody"::text AS EventBody,
    status,
    "createdAt" AS CreatedAt,
    "failureCode" AS FailureCode
FROM payments."payments"
WHERE status = 'Preparing'
OR(status = 'Failed'
    AND "eventBody" ->> 'state' = 'Preparing')
ORDER BY "createdAt", "publicId"
LIMIT 20
