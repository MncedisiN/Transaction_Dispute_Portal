SELECT
    EXISTS (
    SELECT
        1
    FROM payments."payments"
    WHERE "requestId" = @RequestId
    AND "customerPublicId" <> @User)
