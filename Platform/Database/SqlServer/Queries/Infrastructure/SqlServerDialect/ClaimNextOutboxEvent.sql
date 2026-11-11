;

WITH pending AS (
    SELECT
        TOP (1) event_id,
        body,
        attempts,
        state,
        lease_id,
        lease_until
    FROM service_outbox
    WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE (state = 'Pending'
        AND next_attempt_at <= SYSUTCDATETIME())
    OR(state = 'Publishing'
        AND lease_until < SYSUTCDATETIME())
    ORDER BY created_at, event_id)
UPDATE pending
SET
    state = 'Publishing',
    lease_id = @Lease,
    lease_until = DATEADD(second, @LeaseSeconds, SYSUTCDATETIME())
OUTPUT inserted.event_id AS EventId, inserted.body, inserted.attempts
