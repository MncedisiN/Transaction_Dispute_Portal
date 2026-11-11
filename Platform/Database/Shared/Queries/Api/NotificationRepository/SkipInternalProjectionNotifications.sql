UPDATE "notifications"."events"
SET
    "state" = 'Skipped',
    "lastError" = 'internal_projection_event',
    "completedAt" = @Now
WHERE "events"."state" = 'Pending'
AND "events"."kind" IN ('AccountBalanceUpdated', 'AccountActivated', 'TransferReceived', 'PaymentCompleted', 'PaymentFailed')
