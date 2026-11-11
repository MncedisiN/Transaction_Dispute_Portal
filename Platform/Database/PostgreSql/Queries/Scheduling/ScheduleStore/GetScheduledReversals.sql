SELECT
    "publicId" AS RequestId,
    "originalTransactionPublicId" AS OriginalTransactionId,
    kind,
    "actorPublicId" AS ActorId
FROM scheduling."reversals"
WHERE status = 'Scheduled'
AND "dueAt" <= now()
ORDER BY "dueAt" FOR
UPDATE SKIP LOCKED
LIMIT 20
