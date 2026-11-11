UPDATE scheduling."reversals"
SET
    status = @Status,
    "decisionCode" = @Code
WHERE "publicId" = @RequestId
AND status IN ('Scheduled', 'Dispatched')
RETURNING "publicId" AS RequestId, "originalTransactionPublicId" AS OriginalTransactionId, "actorPublicId" AS ActorId, kind AS Kind, status AS Status, "decisionCode" AS Code
