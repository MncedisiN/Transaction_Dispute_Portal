SELECT
    "publicId" AS "Id",
    "transactionPublicId" AS TransactionId,
    reference,
    kind,
    "requestHash" AS RequestHash,
    "requestBody"::text AS RequestBody,
    status,
    id AS InternalId
FROM ledger."journals"
WHERE "transactionPublicId" = @Id
AND status = 'Posted' FOR UPDATE
