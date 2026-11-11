SELECT
    "publicId" AS "Id",
    "paymentBody"::text AS PaymentBody,
    status,
    COALESCE("leaseId", '00000000-0000-0000-0000-000000000000'::uuid) AS LeaseId,
    attempts,
    "failureCode" AS FailureCode,
    "createdAt" AS CreatedAt,
    "updatedAt" AS UpdatedAt
FROM transactions."transactions"
WHERE "customerPublicId" = @User
ORDER BY "createdAt" DESC, "publicId" DESC
LIMIT 250
