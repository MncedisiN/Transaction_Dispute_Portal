SELECT
    "publicId" AS "Id",
    "requestHash" AS RequestHash,
    "eventBody"::text AS EventBody,
    status,
    "createdAt" AS CreatedAt,
    "failureCode" AS FailureCode
FROM payments."payments"
WHERE "customerPublicId" = @User
AND "requestId" = @Id
